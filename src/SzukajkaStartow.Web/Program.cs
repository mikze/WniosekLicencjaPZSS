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

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");

// Lata, dla których klub ma stronę z komunikatami.
api.MapGet("/years", async (StartSearch search, IMemoryCache cache, CancellationToken ct) =>
{
    var years = await cache.GetOrCreateAsync("years", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);
        var pages = await search.GetYearPagesAsync(StartSearch.YearPage(DateTime.Now.Year), ct);
        return pages
            .Select(p => Regex.Match(p, @"komunikaty-(\d{4})"))
            .Where(m => m.Success)
            .Select(m => int.Parse(m.Groups[1].Value))
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();
    });
    return Results.Ok(years);
});

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
    var (plan, athlete) = await BuildPlanAsync(req, search, ct);
    return Results.Ok(Dto.FromPlan(plan, athlete, req.Year));
});

api.MapPost("/license/pdf", async (LicenseRequest req, StartSearch search, CancellationToken ct) =>
{
    if (Validate([req.Name], [req.Year]) is { } error) return error;
    var (plan, athlete) = await BuildPlanAsync(req, search, ct);
    var pdf = LicenseApplication.Render(LicenseApplication.DefaultTemplatePath, LicenseApplication.DefaultFontPath,
        athlete, req.Year, plan.Rows);
    var fileName = $"wniosek-{req.Year}-{string.Join('_', NameMatcher.Tokenize(athlete))}.pdf";
    return Results.File(pdf, "application/pdf", fileName);
});

// Statystyki roku ze wszystkich protokołów. Liczenie trwa kilka sekund, więc wynik trzymamy godzinę w pamięci.
api.MapGet("/ranking", async (int year, StartSearch search, IMemoryCache cache, CancellationToken ct) =>
{
    if (Validate(["ranking"], [year]) is { } error) return error;
    var ranking = await cache.GetOrCreateAsync($"ranking-{year}", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
        var outcome = await search.RunAllAsync([StartSearch.YearPage(year)], ct);
        return RankingBuilder.Build(outcome.Results, year);
    });
    return Results.Ok(ranking);
});

// Wszystkie inne adresy obsługuje aplikacja React.
app.MapFallbackToFile("index.html");

app.Run();

static async Task<(LicensePlan Plan, string Athlete)> BuildPlanAsync(LicenseRequest req, StartSearch search, CancellationToken ct)
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
    return (plan, LicenseApplication.AthleteName(outcome.Results, name));
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
