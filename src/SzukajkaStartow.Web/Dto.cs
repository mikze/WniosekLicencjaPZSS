namespace SzukajkaStartow.Web;

// Kształt danych wymienianych z aplikacją React (JSON, camelCase).

public sealed record SearchRequest(List<string> Names, List<int> Years);

public sealed record SearchResponse(List<StartDto> Results, int PdfCount, List<string> Warnings);

public sealed record DetailDto(string Label, string Value);

public sealed record StartDto(
    string Query,
    string Athlete,
    string? Date,
    string EventName,
    string? DocumentTitle,
    string Competition,
    string? ShootingDate,
    string? Place,
    string? Club,
    string? Result,
    List<DetailDto> Details,
    string PdfUrl,
    int Page);

/// <param name="Licensed">Dyscypliny objęte licencją ("pistolet", "karabin", "strzelba"); puste = wykryj automatycznie.</param>
/// <param name="Leading">Dyscyplina wiodąca; null = wybierz automatycznie.</param>
/// <param name="AllStarts">true = wpisz wszystkie starty z roku; false = tylko tyle, ile trzeba do spełnienia warunków.</param>
public sealed record LicenseRequest(string Name, int Year, List<string>? Licensed, string? Leading, bool OneXPerRow,
    bool AllStarts = false);

public sealed record LicenseRowDto(string Name, string Date, string? Place, bool Pistol, bool Rifle, bool Shotgun, string? Note);

public sealed record DisciplineCountDto(string Discipline, int Count, int? Required, bool Leading);

public sealed record MissingDto(string Discipline, int Count);

public sealed record LicensePlanDto(
    string Athlete,
    int Year,
    List<LicenseRowDto> Rows,
    int AvailableRows,
    string Leading,
    List<string> Licensed,
    List<DisciplineCountDto> Counts,
    bool MeetsRequirements,
    List<MissingDto> Missing,
    List<MissingDto>? MissingForFullLicense);

public static class Dto
{
    public static StartDto FromStart(StartResult r) => new(
        r.Query,
        r.Athlete,
        r.Date,
        r.Source.EventName ?? r.Source.LinkText,
        r.DocumentTitle,
        r.Competition,
        r.ShootingDate,
        r.Place,
        r.Club,
        r.Result,
        r.Details.Select(d => new DetailDto(d.Key, d.Value)).ToList(),
        r.Source.Url,
        r.Page);

    public static LicensePlanDto FromPlan(LicensePlan plan, string athlete, int year, int availableRows) => new(
        athlete,
        year,
        plan.Rows.Select(r => new LicenseRowDto(r.Name, r.Date, r.Place, r.Pistol, r.Rifle, r.Shotgun, r.Note)).ToList(),
        availableRows,
        Name(plan.Leading),
        LicenseApplication.AllDisciplines.Where(d => plan.Licensed.HasFlag(d)).Select(Name).ToList(),
        LicenseApplication.AllDisciplines.Select(d => new DisciplineCountDto(
            Name(d), plan.Counts[d], plan.Required.TryGetValue(d, out var req) ? req : null, d == plan.Leading)).ToList(),
        plan.MeetsRequirements,
        Missing(plan),
        plan.FullLicensePlan is { } full ? Missing(full) : null);

    private static List<MissingDto> Missing(LicensePlan plan) => plan.Required
        .Where(r => plan.Counts[r.Key] < r.Value)
        .Select(r => new MissingDto(Name(r.Key), r.Value - plan.Counts[r.Key]))
        .ToList();

    public static string Name(Discipline d) => d switch
    {
        Discipline.Pistol => "pistolet",
        Discipline.Rifle => "karabin",
        Discipline.Shotgun => "strzelba",
        _ => d.ToString(),
    };

    public static Discipline? ParseDiscipline(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "pistolet" => Discipline.Pistol,
        "karabin" => Discipline.Rifle,
        "strzelba" => Discipline.Shotgun,
        _ => null,
    };
}
