using System.Text;

namespace SzukajkaStartow;

public static class Report
{
    public static void PrintConsole(List<StartResult> results, List<string> queries, bool verbose)
    {
        foreach (var query in queries)
        {
            var mine = results.Where(r => r.Query == query).ToList();
            Console.WriteLine();
            Console.WriteLine(new string('=', 78));
            Console.WriteLine($" {query}  —  starty: {mine.Count}");
            Console.WriteLine(new string('=', 78));
            if (mine.Count == 0)
            {
                Console.WriteLine("  (nie znaleziono)");
                continue;
            }

            var names = mine.GroupBy(r => string.Join(' ', NameMatcher.Tokenize(r.Athlete).Order()))
                .Select(g => g.First().Athlete).ToList();
            if (names.Count > 1)
                Console.WriteLine($"  Uwaga: pasuje kilka osób: {string.Join(", ", names)}");

            foreach (var doc in mine.GroupBy(r => r.Source.Url))
            {
                var first = doc.First();
                Console.WriteLine();
                Console.WriteLine($"▶ {first.Date ?? "?"}  {first.Source.EventName ?? first.Source.LinkText}");
                if (first.DocumentTitle is { } t)
                    Console.WriteLine($"  dokument: {t}");
                Console.WriteLine($"  {first.Source.Url}");

                foreach (var r in doc)
                {
                    var place = r.Place is null ? "   -" : $"{r.Place,4}.";
                    var athlete = names.Count > 1 || verbose ? $" [{r.Athlete}]" : "";
                    var club = r.Club is null ? "" : $"  ({r.Club})";
                    Console.WriteLine($"   {place}  {r.Competition}{athlete}");
                    Console.WriteLine($"          wynik: {r.Result ?? "?"}{club}{(r.ShootingDate is null || SameDate(r.ShootingDate, r.Date) ? "" : $"  · {r.ShootingDate}")}");
                    if (verbose)
                    {
                        var details = FormatDetails(r);
                        if (details.Length > 0) Console.WriteLine($"          szczegóły: {details}");
                        Console.WriteLine($"          PDF (str. {r.Page}): {r.RawText}");
                    }
                }
            }
        }
    }

    public static void WriteCsv(List<StartResult> results, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', "Szukany", "Data zawodów", "Zawody", "Dokument", "Konkurencja",
            "Data strzelania", "Miejsce", "Zawodnik", "Klub", "Wynik", "Szczegóły", "Strona", "PDF"));
        foreach (var r in results)
        {
            sb.AppendLine(string.Join(';', new[]
            {
                r.Query, r.Date, r.Source.EventName ?? r.Source.LinkText, r.DocumentTitle, r.Competition,
                r.ShootingDate, r.Place, r.Athlete, r.Club, r.Result, FormatDetails(r), r.Page.ToString(), r.Source.Url,
            }.Select(Escape)));
        }
        // BOM, żeby Excel poprawnie pokazał polskie znaki.
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static bool SameDate(string a, string? b) =>
        b is not null && a.Replace(" ", "").TrimEnd('.', 'r') == b.Replace(" ", "").TrimEnd('.', 'r');

    private static string FormatDetails(StartResult r) =>
        string.Join(", ", r.Details.Select(d => d.Key.Length > 0 ? $"{d.Key}: {d.Value}" : d.Value));

    private static string Escape(string? s)
    {
        s ??= "";
        return s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
