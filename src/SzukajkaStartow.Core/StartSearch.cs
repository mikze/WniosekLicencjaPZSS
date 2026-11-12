using System.Collections.Concurrent;

namespace SzukajkaStartow;

/// <summary>Wynik wyszukiwania: starty (w kolejności zawodników, komunikatów i protokołów) oraz ostrzeżenia.</summary>
public sealed record SearchOutcome(
    List<StartResult> Results,
    List<PdfLink> Links,
    List<string> Warnings);

/// <summary>
/// Całe wyszukiwanie w jednym miejscu: strony z komunikatami → linki do PDF → pobranie (z cache) →
/// odczyt tabel → dopasowanie zawodników. Używane przez wersję konsolową i webową.
/// </summary>
public sealed class StartSearch(HttpClient http, string cacheDir)
{
    public const string DefaultBase = "https://agat.zlotoryja.pl/komunikaty/komunikaty-{0}/";

    public static string YearPage(int year) => string.Format(DefaultBase, year);

    public static string DefaultCacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SzukajkaStartow", "cache");

    private readonly WebScraper _scraper = new(http);

    // Odczytane linie PDF-ów nie zależą od szukanych nazwisk — trzymamy je w pamięci między wyszukiwaniami.
    private readonly ConcurrentDictionary<string, Lazy<Task<List<TextLine>>>> _lines = new();

    /// <summary>Strony z komunikatami ze wszystkich lat podlinkowanych na podanej stronie.</summary>
    public async Task<List<string>> GetYearPagesAsync(string pageUrl, CancellationToken ct) =>
        await _scraper.GetYearPagesAsync(pageUrl, ct);

    public Task<SearchOutcome> RunAsync(
        IReadOnlyList<string> names,
        IReadOnlyList<string> pages,
        bool refresh = false,
        Action<string>? log = null,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default) =>
        RunCoreAsync(names.Select(n => new NameMatcher(n)).ToList(), names, pages, refresh, log, progress, ct);

    /// <summary>Wszyscy zawodnicy ze wszystkich protokołów na podanych stronach (do rankingu).</summary>
    public Task<SearchOutcome> RunAllAsync(IReadOnlyList<string> pages, CancellationToken ct = default) =>
        RunCoreAsync(null, [], pages, false, null, null, ct);

    private async Task<SearchOutcome> RunCoreAsync(
        IReadOnlyList<NameMatcher>? matchers,
        IReadOnlyList<string> names,
        IReadOnlyList<string> pages,
        bool refresh,
        Action<string>? log,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken ct)
    {
        var cache = new PdfCache(http, cacheDir, refresh);
        var warnings = new ConcurrentBag<string>();

        var links = new List<PdfLink>();
        foreach (var page in pages)
        {
            try
            {
                var found = await _scraper.GetPdfLinksAsync(page, ct);
                log?.Invoke($"{page}: {found.Count} PDF");
                links.AddRange(found);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                warnings.Add($"Nie udało się pobrać strony {page}: {ex.Message}");
            }
        }

        var all = new ConcurrentBag<StartResult>();
        int done = 0;
        await Parallel.ForEachAsync(links, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (link, token) =>
            {
                try
                {
                    var lines = await ReadLinesAsync(cache, link.Url, refresh, token);
                    if (lines.Count == 0)
                        warnings.Add($"Brak tekstu (skan?): {link.Url}");
                    var (_, results) = ResultExtractor.Extract(lines, matchers, link);
                    foreach (var r in results) all.Add(r);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Błąd {link.Url}: {ex.Message}");
                }
                progress?.Report((Interlocked.Increment(ref done), links.Count));
            });

        var nameOrder = names.ToList();
        var ordered = all
            .OrderBy(r => nameOrder.IndexOf(r.Query))
            .ThenBy(r => links.IndexOf(r.Source))
            .ThenBy(r => r.LineIndex)
            .ToList();

        return new SearchOutcome(ordered, links, warnings.OrderBy(w => w).ToList());
    }

    private async Task<List<TextLine>> ReadLinesAsync(PdfCache cache, string url, bool refresh, CancellationToken ct)
    {
        if (refresh) _lines.TryRemove(url, out _);
        var lazy = _lines.GetOrAdd(url, u => new Lazy<Task<List<TextLine>>>(async () =>
            PdfLayout.ReadLines(await cache.GetAsync(u, CancellationToken.None))));
        try
        {
            return await lazy.Value.WaitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lines.TryRemove(url, out _); // nie zapamiętuj błędu — następne wyszukiwanie spróbuje ponownie
            throw;
        }
    }
}
