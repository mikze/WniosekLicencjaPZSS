using System.Text.RegularExpressions;

namespace SzukajkaStartow;

/// <param name="LicenseScope">Zakres licencji, dla którego spełnia warunki PZSS (np. "pistolet + karabin"); null = nie spełnia.</param>
public sealed record AthleteStat(
    string Name,
    string? Club,
    int Starts,
    int Events,
    int Gold,
    int Silver,
    int Bronze,
    int Pistol,
    int Rifle,
    int Shotgun,
    bool MeetsLicense,
    string? LicenseScope);

public sealed record TopResult(string Athlete, string? Club, string Result, string? Place, string? Date, string EventName);

/// <param name="Scores">Wyniki wszystkich startów w tej konkurencji (do rozkładu punktów).</param>
public sealed record CompetitionStat(string Name, int Starters, int Starts, int Events, List<TopResult> Top, List<double> Scores);

public sealed record ClubStat(string Club, int Athletes, int Starts, int Podiums);

public sealed record EventStat(string? Date, string Name, string PdfUrl, int Athletes, int Starts);

public sealed record Ranking(
    int Year,
    int Events,
    int Starts,
    int AthleteCount,
    int ClubCount,
    int MeetingLicense,
    List<AthleteStat> Athletes,
    List<CompetitionStat> Competitions,
    List<ClubStat> Clubs,
    List<EventStat> EventList);

/// <summary>Statystyki roku ze wszystkich protokołów: aktywność, podia, licencje, najlepsze wyniki, kluby, frekwencja.</summary>
public static partial class RankingBuilder
{
    private const int TopPerCompetition = 10;

    /// <summary>Ten sam zawodnik zapisany różnie ("KOWALSKI, Jan", "Kowalski Jan") ma ten sam klucz.</summary>
    public static string AthleteKey(string athlete) => string.Join(' ', NameMatcher.Tokenize(athlete).Order());

    public static Ranking Build(IReadOnlyList<StartResult> all, int year)
    {
        // Liczymy tylko faktyczne starty (z wynikiem albo miejscem), bez DNS/DNF.
        var starts = all
            .Where(r => r.Result is not null && !ResultExtractor.IsStatus(r.Result) || ParsePlace(r.Place) is not null)
            .Select(r => r with { Query = AthleteKey(r.Athlete) })
            .Where(r => r.Query.Length > 0)
            .ToList();

        var byAthlete = starts.GroupBy(r => r.Query).ToList();

        var athletes = byAthlete.Select(g =>
        {
            var list = g.ToList();
            var places = list.Select(r => ParsePlace(r.Place)).ToList();
            var plan = LicenseApplication.BuildRows(list, year);
            return new AthleteStat(
                DisplayName(list),
                MostCommon(list.Select(r => r.Club)),
                list.Count,
                list.Select(r => r.Source.Url).Distinct().Count(),
                places.Count(p => p == 1),
                places.Count(p => p == 2),
                places.Count(p => p == 3),
                plan.Counts[Discipline.Pistol],
                plan.Counts[Discipline.Rifle],
                plan.Counts[Discipline.Shotgun],
                plan.MeetsRequirements,
                plan.MeetsRequirements ? ScopeName(plan.Licensed) : null);
        })
            .OrderByDescending(a => a.Starts)
            .ThenByDescending(a => a.Gold + a.Silver + a.Bronze)
            .ThenBy(a => a.Name, StringComparer.Create(new System.Globalization.CultureInfo("pl-PL"), true))
            .ToList();

        var names = byAthlete.ToDictionary(g => g.Key, g => DisplayName(g.ToList()));

        var competitions = starts
            .GroupBy(r => CompetitionKey(r.Competition))
            .Select(g =>
            {
                // Najlepszy wynik każdego zawodnika w tej konkurencji.
                var best = g.Where(r => ParseScore(r.Result) is not null)
                    .GroupBy(r => r.Query)
                    .Select(a => a.MaxBy(r => ParseScore(r.Result))!)
                    .OrderByDescending(r => ParseScore(r.Result))
                    .ThenBy(r => ParsePlace(r.Place) ?? int.MaxValue)
                    .Take(TopPerCompetition)
                    .Select(r => new TopResult(names[r.Query], r.Club, r.Result!, r.Place, CleanDate(r.Date),
                        r.DocumentTitle ?? r.Source.EventName ?? r.Source.LinkText))
                    .ToList();
                return new CompetitionStat(
                    MostCommon(g.Select(r => r.Competition))!,
                    g.Select(r => r.Query).Distinct().Count(),
                    g.Count(),
                    g.Select(r => r.Source.Url).Distinct().Count(),
                    best,
                    g.Select(r => ParseScore(r.Result)).OfType<double>().Order().ToList());
            })
            .Where(c => c.Top.Count > 0)
            .OrderByDescending(c => c.Starters)
            .ToList();

        var clubs = starts
            .Where(r => !string.IsNullOrWhiteSpace(r.Club))
            .GroupBy(r => NormalizeClub(r.Club!))
            .Select(g => new ClubStat(
                MostCommon(g.Select(r => r.Club))!,
                g.Select(r => r.Query).Distinct().Count(),
                g.Count(),
                g.Count(r => ParsePlace(r.Place) is >= 1 and <= 3)))
            .OrderByDescending(c => c.Athletes)
            .ThenByDescending(c => c.Starts)
            .ToList();

        var events = starts
            .GroupBy(r => r.Source.Url)
            .Select(g => new EventStat(
                CleanDate(g.First().Date),
                g.First().DocumentTitle ?? g.First().Source.EventName ?? g.First().Source.LinkText,
                g.Key,
                g.Select(r => r.Query).Distinct().Count(),
                g.Count()))
            .OrderBy(e => SortableDate(e.Date))
            .ToList();

        return new Ranking(
            year,
            events.Count,
            starts.Count,
            athletes.Count,
            clubs.Count,
            athletes.Count(a => a.MeetsLicense),
            athletes,
            competitions,
            clubs,
            events);
    }

    /// <summary>Wynik jako liczba ("93,9" → 93.9, "577-28x" → 577).</summary>
    public static double? ParseScore(string? result)
    {
        if (result is null) return null;
        var m = LeadingNumberRegex().Match(result);
        return m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    public static int? ParsePlace(string? place) =>
        int.TryParse(place?.TrimEnd('.'), out var p) && p > 0 ? p : null;

    /// <summary>Najczęstszy zapis nazwiska, bez przecinka ("KOWALSKI, Jan" → "KOWALSKI Jan").</summary>
    private static string DisplayName(List<StartResult> list) =>
        MostCommon(list.Select(r => SpacesRegex().Replace(r.Athlete.Replace(",", " "), " ").Trim()))!;

    private static string? MostCommon(IEnumerable<string?> values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim())
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

    private static string CompetitionKey(string c) => SpacesRegex().Replace(c.ToLowerInvariant(), " ").Trim();

    private static string NormalizeClub(string c) =>
        string.Join(' ', NameMatcher.Tokenize(c));

    private static string ScopeName(Discipline licensed) => string.Join(" + ",
        LicenseApplication.AllDisciplines.Where(d => licensed.HasFlag(d)).Select(d => d switch
        {
            Discipline.Pistol => "pistolet",
            Discipline.Rifle => "karabin",
            _ => "strzelba",
        }));

    private static string? CleanDate(string? date) => date is null ? null : TrailingRRegex().Replace(date.Trim(), "");

    /// <summary>"08-10.05.2026" → "2026-05-08" do sortowania.</summary>
    private static string SortableDate(string? date)
    {
        var m = date is null ? Match.Empty : DateRegex().Match(date);
        return m.Success ? $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}" : "9999";
    }

    [GeneratedRegex(@"^\s*(\d+(?:[.,]\d+)?)")]
    private static partial Regex LeadingNumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"\s*r\.?$")]
    private static partial Regex TrailingRRegex();

    [GeneratedRegex(@"(\d{1,2})(?:\s*[-–]\s*\d{1,2})?\.(\d{1,2})\.(\d{4})")]
    private static partial Regex DateRegex();
}
