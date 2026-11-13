using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using SzukajkaStartow;
using SzukajkaStartow.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("agat", c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("SzukajkaStartow/1.0");
});
// Jedna instancja wyszukiwarki na cały serwer — trzyma w pamięci odczytane PDF-y.
builder.Services.AddSingleton(sp =>
{
    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("agat");
    var cacheDir = builder.Configuration["CacheDir"] ?? StartSearch.DefaultCacheDir;
    return new StartSearch(http, cacheDir);
});

builder.Services.AddSingleton<YearData>();

var app = builder.Build();

// Po starcie wczytujemy w tle wszystkie lata (ranking i karta zawodnika potrzebują wszystkich wyników).
app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(() => app.Services.GetRequiredService<YearData>().WarmUpAsync()));

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// Lata, dla których klub ma stronę z komunikatami.
api.MapGet("/years", async (YearData data) => Results.Ok(await data.GetYearsAsync()));

api.MapPost("/search", async (SearchRequest req, StartSearch search, CancellationToken ct) =>
{
    if (Validate(req.Names, req.Years) is { } error) return error;

    var names = Clean(req.Names);
    var pages = req.Years.Distinct().OrderByDescending(y => y).Select(StartSearch.YearPage).ToList();
    var outcome = await search.RunAsync(names, pages, ct: ct);

    return Results.Ok(new SearchResponse(
        outcome.Results.Select(Dto.FromStart).ToList(),
        outcome.Links.Count,
        outcome.Warnings));
});

api.MapPost("/license", async (LicenseRequest req, StartSearch search, CancellationToken ct) =>
{
    if (Validate([req.Name], [req.Year]) is { } error) return error;
    var (plan, athlete, available) = await BuildPlanAsync(req, search, ct);
    return Results.Ok(Dto.FromPlan(plan, athlete, req.Year, available));
});

api.MapPost("/license/pdf", async (LicenseRequest req, StartSearch search, CancellationToken ct) =>
{
    if (Validate([req.Name], [req.Year]) is { } error) return error;
    var (plan, athlete, _) = await BuildPlanAsync(req, search, ct);
    var pdf = LicenseApplication.Render(LicenseApplication.DefaultTemplatePath, LicenseApplication.DefaultFontPath,
        athlete, req.Year, plan.Rows);
    var fileName = $"wniosek-{req.Year}-{string.Join('_', NameMatcher.Tokenize(athlete))}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
});

// Statystyki roku ze wszystkich protokołów, opcjonalnie tylko dla wybranych klubów (?club=...&club=...).
api.MapGet("/ranking", async (int year, [FromQuery(Name = "club")] string[]? club, YearData data, IMemoryCache cache,
    CancellationToken ct) =>
{
    if (Validate(["ranking"], [year]) is { } error) return error;
    var clubs = (club ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase).ToList();
    if (clubs.Count > 30 || clubs.Any(c => c.Length > 100))
        return Results.BadRequest(new { error = "Nieprawidłowy filtr klubów." });

    var all = await data.GetAllAsync(year, ct);
    var ranking = cache.GetOrCreate($"ranking-{year}-{all.GetHashCode()}-{string.Join('|', clubs)}", entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        return RankingBuilder.Build(all, year, clubs);
    });
    return Results.Ok(ranking);
});

// Karta zawodnika z wybranych lat (domyślnie wszystkich). Gdy nazwisko pasuje do kilku osób — lista do wyboru.
api.MapGet("/athlete", async (string name, [FromQuery(Name = "year")] int[]? year, YearData data, CancellationToken ct) =>
{
    var years = year is { Length: > 0 } ? year.Distinct().ToList() : await data.GetYearsAsync();
    if (Validate([name], years) is { } error) return error;
    var all = new Dictionary<int, List<StartResult>>();
    foreach (var y in years)
        all[y] = await data.GetAllAsync(y, ct);
    return Results.Ok(AthleteProfileBuilder.Build(all, name.Trim()));
});

// Wszystkie inne adresy obsługuje aplikacja React.
app.MapFallbackToFile("index.html");

app.Run();

static async Task<(LicensePlan Plan, string Athlete, int AvailableRows)> BuildPlanAsync(LicenseRequest req, StartSearch search, CancellationToken ct)
{
    var name = req.Name.Trim();
    var outcome = await search.RunAsync([name], [StartSearch.YearPage(req.Year)], ct: ct);
    var leading = Dto.ParseDiscipline(req.Leading);
    Discipline? licensed = req.Licensed is { Count: > 0 }
        ? req.Licensed.Aggregate(Discipline.None, (acc, d) => acc | (Dto.ParseDiscipline(d) ?? Discipline.None))
        : null;

    var plan = LicenseApplication.BuildRows(outcome.Results, req.Year, leading, licensed);
    if (req.OneXPerRow)
        plan = LicenseApplication.OneDisciplinePerRow(plan);
    var available = plan.Rows.Count;
    if (!req.AllStarts)
        plan = LicenseApplication.TrimToRequirements(plan);
    return (plan, LicenseApplication.AthleteName(outcome.Results, name), available);
}

static List<string> Clean(IEnumerable<string> names) =>
    names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

static IResult? Validate(IEnumerable<string>? names, IEnumerable<int>? years)
{
    var cleaned = Clean(names ?? []);
    var yearList = (years ?? []).ToList();
    if (cleaned.Count == 0) return Results.BadRequest(new { error = "Podaj przynajmniej jednego zawodnika." });
    if (cleaned.Count > 20) return Results.BadRequest(new { error = "Maksymalnie 20 zawodników naraz." });
    if (cleaned.Any(n => n.Length > 80 || NameMatcher.Tokenize(n).Length == 0))
        return Results.BadRequest(new { error = "Nieprawidłowe imię lub nazwisko." });
    if (yearList.Count == 0) return Results.BadRequest(new { error = "Wybierz przynajmniej jeden rok." });
    // Adresy stron budujemy tylko z roku, więc serwer nie pobierze niczego spoza strony klubu.
    if (yearList.Any(y => y < 2015 || y > DateTime.Now.Year + 1))
        return Results.BadRequest(new { error = "Nieprawidłowy rok." });
    return null;
}
