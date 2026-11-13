using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;

namespace SzukajkaStartow.Web;

/// <summary>
/// Lista lat i wszystkie starty z danego roku (tryb „wszyscy zawodnicy”), trzymane w pamięci.
/// Ten sam rok nie jest wczytywany dwa razy naraz, a anulowanie jednego zapytania nie przerywa wczytywania dla innych.
/// </summary>
public sealed partial class YearData(StartSearch search, IMemoryCache cache, ILogger<YearData> log)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public async Task<List<int>> GetYearsAsync() =>
        (await cache.GetOrCreateAsync("years", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
            var pages = await search.GetYearPagesAsync(StartSearch.YearPage(DateTime.Now.Year), CancellationToken.None);
            return pages
                .Select(p => YearRegex().Match(p))
                .Where(m => m.Success)
                .Select(m => int.Parse(m.Groups[1].Value))
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();
        }))!;

    public async Task<List<StartResult>> GetAllAsync(int year, CancellationToken ct)
    {
        var key = $"all-{year}";
        if (cache.TryGetValue(key, out List<StartResult>? cached)) return cached!;

        var gate = _locks.GetOrAdd(year, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(key, out cached)) return cached!;
            var all = (await search.RunAllAsync([StartSearch.YearPage(year)], CancellationToken.None)).Results;
            cache.Set(key, all, Lifetime);
            return all;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Wczytuje wszystkie lata w tle po starcie serwera, żeby pierwszy użytkownik nie czekał.</summary>
    public async Task WarmUpAsync()
    {
        try
        {
            foreach (var year in await GetYearsAsync())
                await GetAllAsync(year, CancellationToken.None);
            log.LogInformation("Wczytano starty ze wszystkich lat");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Wstępne wczytanie danych nie powiodło się — dane wczytają się przy pierwszym zapytaniu");
        }
    }

    [GeneratedRegex(@"komunikaty-(\d{4})")]
    private static partial Regex YearRegex();
}
