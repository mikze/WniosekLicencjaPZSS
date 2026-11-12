using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace SzukajkaStartow;

/// <summary>Czyta stronę z komunikatami i wyciąga linki do PDF-ów wraz z datą i nazwą zawodów.</summary>
public sealed partial class WebScraper(HttpClient http)
{
    public async Task<List<PdfLink>> GetPdfLinksAsync(string pageUrl, CancellationToken ct)
    {
        var html = await http.GetStringAsync(pageUrl, ct);
        var doc = new HtmlParser().ParseDocument(html);
        var baseUri = new Uri(pageUrl);
        var result = new List<PdfLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Główny przypadek: tabela "Lp | Data zawodów | Nazwa zawodów | Konkurencja | Pobierz".
        foreach (var row in doc.QuerySelectorAll("table tr"))
        {
            var cells = row.QuerySelectorAll("td").Select(c => Clean(c.TextContent)).ToArray();
            var links = row.QuerySelectorAll("a[href]")
                .Where(a => IsPdf(a.GetAttribute("href")))
                .ToList();
            if (links.Count == 0) continue;

            string? date = cells.FirstOrDefault(c => DateRegex().IsMatch(c));
            string? name = date is not null && Array.IndexOf(cells, date) + 1 < cells.Length
                ? cells[Array.IndexOf(cells, date) + 1]
                : cells.OrderByDescending(c => c.Length).FirstOrDefault();

            foreach (var a in links)
            {
                var url = new Uri(baseUri, a.GetAttribute("href")!).AbsoluteUri;
                if (seen.Add(url))
                    result.Add(new PdfLink(url, date, name, Clean(a.TextContent), pageUrl));
            }
        }

        // Zapas: PDF-y poza tabelą.
        foreach (var a in doc.QuerySelectorAll("a[href]").Where(a => IsPdf(a.GetAttribute("href"))))
        {
            var url = new Uri(baseUri, a.GetAttribute("href")!).AbsoluteUri;
            if (seen.Add(url))
                result.Add(new PdfLink(url, null, null, Clean(a.TextContent), pageUrl));
        }

        return result;
    }

    /// <summary>Znajduje strony z komunikatami z innych lat (linki ".../komunikaty-RRRR/").</summary>
    public async Task<List<string>> GetYearPagesAsync(string pageUrl, CancellationToken ct)
    {
        var html = await http.GetStringAsync(pageUrl, ct);
        var doc = new HtmlParser().ParseDocument(html);
        var baseUri = new Uri(pageUrl);
        return doc.QuerySelectorAll("a[href]")
            .Select(a => a.GetAttribute("href")!)
            .Where(h => YearPageRegex().IsMatch(h))
            .Select(h => new Uri(baseUri, h).AbsoluteUri)
            .Append(pageUrl)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(u => u)
            .ToList();
    }

    private static bool IsPdf(string? href) =>
        href is not null && href.Split('?', '#')[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string s) =>
        WhitespaceRegex().Replace(System.Net.WebUtility.HtmlDecode(s), " ").Trim().Trim('<', '>').Trim();

    [GeneratedRegex(@"\d{1,2}(-\d{1,2})?\.\d{1,2}\.\d{4}")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"komunikaty-\d{4}/?$", RegexOptions.IgnoreCase)]
    private static partial Regex YearPageRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
