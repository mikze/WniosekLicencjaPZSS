namespace SzukajkaStartow;

public sealed record AthleteCandidate(string Name, string? Club, int Starts);

/// <param name="Field">Liczba zawodników w tej konkurencji na tych zawodach.</param>
/// <param name="Percentile">Jaki procent stawki wyprzedził (0–100); null gdy startował sam.</param>
/// <param name="Group">Wspólna nazwa konkurencji z różnych lat ("Psp 10" i "Pistolet sportowy 10 strzałów" to ta sama).</param>
public sealed record ProfileStart(
    int Year,
    string? Date,
    string SortDate,
    string EventName,
    string Competition,
    string? Place,
    string? Result,
    double? Score,
    int Field,
    double? Percentile,
    string PdfUrl,
    string Group);

public sealed record PersonalRecord(
    string Competition,
    string Best,
    string? BestDate,
    string BestEvent,
    int Count,
    double Average,
    string? Last,
    string? LastDate);

public sealed record SeasonStat(int Year, int Starts, int Events, int Gold, int Silver, int Bronze, bool MeetsLicense, string? LicenseScope);

public sealed record Rival(string Name, string? Club, int Common, int Wins, int Losses);

public sealed record AthleteProfile(
    string Name,
    string? Club,
    int Starts,
    int Events,
    int Gold,
    int Silver,
    int Bronze,
    int? BestPlace,
    double? AveragePercentile,
    List<SeasonStat> Seasons,
    List<PersonalRecord> Records,
    List<Rival> Rivals,
    List<ProfileStart> History);

/// <summary>Wynik szukania karty: profil albo lista osób do wyboru, gdy zapytanie pasuje do kilku zawodników.</summary>
public sealed record AthleteProfileResult(AthleteProfile? Profile, List<AthleteCandidate> Candidates);

/// <summary>
/// Karta zawodnika z wyników wszystkich zawodników (tryb „wszyscy”), dzięki czemu znamy też wielkość stawki
/// w każdej konkurencji i rywali.
/// </summary>
public static class AthleteProfileBuilder
{
    private sealed record Entry(StartResult Start, int Year, string Key);

    public static AthleteProfileResult Build(IReadOnlyDictionary<int, List<StartResult>> allByYear, string query)
    {
        var matcher = new NameMatcher(query);

        // Tylko faktyczne starty (z wynikiem albo miejscem).
        var entries = allByYear
            .SelectMany(kv => kv.Value
                .Where(r => r.Result is not null && !ResultExtractor.IsStatus(r.Result) || RankingBuilder.ParsePlace(r.Place) is not null)
                .Select(r => new Entry(r, kv.Key, RankingBuilder.AthleteKey(r.Athlete))))
            .Where(e => e.Key.Length > 0)
            .ToList();

        var matching = entries.Where(e => matcher.Matches(e.Start.Athlete)).GroupBy(e => e.Key).ToList();
        if (matching.Count == 0)
            return new AthleteProfileResult(null, []);

        var exactKey = RankingBuilder.AthleteKey(query);
        var chosen = matching.Count == 1 ? matching[0] : matching.FirstOrDefault(g => g.Key == exactKey);
        if (chosen is null)
        {
            var candidates = matching
                .Select(g => new AthleteCandidate(
                    RankingBuilder.DisplayName(g.Select(e => e.Start).ToList()),
                    RankingBuilder.MostCommon(g.Select(e => e.Start.Club)),
                    g.Count()))
                .OrderByDescending(c => c.Starts)
                .ToList();
            return new AthleteProfileResult(null, candidates);
        }

        var key = chosen.Key;
        var mine = chosen.OrderBy(e => RankingBuilder.SortableDate(e.Start.Date)).ThenBy(e => e.Start.LineIndex).ToList();

        // Stawka każdej konkurencji: wszyscy zawodnicy z tego samego protokołu.
        var groups = entries
            .GroupBy(e => (e.Start.Source.Url, e.Start.Competition))
            .ToDictionary(g => g.Key, g => g.GroupBy(e => e.Key).Select(a => a.First()).ToList());

        var history = new List<ProfileStart>();
        var rivals = new Dictionary<string, (List<StartResult> Starts, int Common, int Wins, int Losses)>();
        foreach (var e in mine)
        {
            var field = groups[(e.Start.Source.Url, e.Start.Competition)];
            var place = RankingBuilder.ParsePlace(e.Start.Place);
            int? rank = place ?? RankByScore(field, e);
            double? percentile = field.Count > 1 && rank is { } p ? Math.Clamp((field.Count - p) * 100.0 / (field.Count - 1), 0, 100) : null;

            history.Add(new ProfileStart(
                e.Year,
                RankingBuilder.CleanDate(e.Start.Date),
                RankingBuilder.SortableDate(e.Start.Date),
                e.Start.DocumentTitle ?? e.Start.Source.EventName ?? e.Start.Source.LinkText,
                e.Start.Competition,
                e.Start.Place,
                e.Start.Result,
                RankingBuilder.ParseScore(e.Start.Result),
                field.Count,
                percentile,
                e.Start.Source.Url,
                RankingBuilder.CanonicalCompetition(e.Start.Competition)));

            foreach (var other in field.Where(o => o.Key != key))
            {
                (List<StartResult> Starts, int Common, int Wins, int Losses) cur =
                    rivals.TryGetValue(other.Key, out var v) ? v : (new List<StartResult>(), 0, 0, 0);
                cur.Starts.Add(other.Start);
                cur.Common++;
                var cmp = Compare(e.Start, other.Start);
                if (cmp > 0) cur.Wins++;
                else if (cmp < 0) cur.Losses++;
                rivals[other.Key] = cur;
            }
        }

        var records = mine
            .Where(e => RankingBuilder.ParseScore(e.Start.Result) is not null)
            .GroupBy(e => RankingBuilder.CanonicalCompetition(e.Start.Competition))
            .Select(g =>
            {
                var best = g.MaxBy(e => RankingBuilder.ParseScore(e.Start.Result))!;
                var last = g.Last();
                return new PersonalRecord(
                    g.Key,
                    best.Start.Result!,
                    RankingBuilder.CleanDate(best.Start.Date),
                    best.Start.DocumentTitle ?? best.Start.Source.EventName ?? best.Start.Source.LinkText,
                    g.Count(),
                    Math.Round(g.Average(e => RankingBuilder.ParseScore(e.Start.Result)!.Value), 1),
                    last.Start.Result,
                    RankingBuilder.CleanDate(last.Start.Date));
            })
            .OrderByDescending(r => r.Count)
            .ToList();

        var seasons = mine.GroupBy(e => e.Year).OrderByDescending(g => g.Key).Select(g =>
        {
            var starts = g.Select(e => e.Start).ToList();
            var places = starts.Select(r => RankingBuilder.ParsePlace(r.Place)).ToList();
            var plan = LicenseApplication.BuildRows(starts, g.Key);
            return new SeasonStat(
                g.Key,
                starts.Count,
                starts.Select(r => r.Source.Url).Distinct().Count(),
                places.Count(p => p == 1),
                places.Count(p => p == 2),
                places.Count(p => p == 3),
                plan.MeetsRequirements,
                plan.MeetsRequirements ? RankingBuilder.ScopeName(plan.Licensed) : null);
        }).ToList();

        var rivalList = rivals
            .Where(kv => kv.Value.Common >= 2)
            .OrderByDescending(kv => kv.Value.Common)
            .ThenByDescending(kv => kv.Value.Wins + kv.Value.Losses)
            .Take(10)
            .Select(kv => new Rival(
                RankingBuilder.DisplayName(kv.Value.Starts),
                RankingBuilder.MostCommon(kv.Value.Starts.Select(r => r.Club)),
                kv.Value.Common,
                kv.Value.Wins,
                kv.Value.Losses))
            .ToList();

        var allPlaces = mine.Select(e => RankingBuilder.ParsePlace(e.Start.Place)).ToList();
        var percentiles = history.Select(h => h.Percentile).OfType<double>().ToList();
        var profile = new AthleteProfile(
            RankingBuilder.DisplayName(mine.Select(e => e.Start).ToList()),
            RankingBuilder.MostCommon(mine.Select(e => e.Start.Club)),
            mine.Count,
            mine.Select(e => e.Start.Source.Url).Distinct().Count(),
            allPlaces.Count(p => p == 1),
            allPlaces.Count(p => p == 2),
            allPlaces.Count(p => p == 3),
            allPlaces.OfType<int>().Any() ? allPlaces.OfType<int>().Min() : null,
            percentiles.Count > 0 ? Math.Round(percentiles.Average(), 0) : null,
            seasons,
            records,
            rivalList,
            history);
        return new AthleteProfileResult(profile, []);
    }

    /// <summary>Miejsce wyliczone z wyniku, gdy protokół nie podaje miejsca (wyższy wynik = lepiej).</summary>
    private static int? RankByScore(List<Entry> field, Entry me)
    {
        var score = RankingBuilder.ParseScore(me.Start.Result);
        if (score is null) return null;
        return 1 + field.Count(o => RankingBuilder.ParseScore(o.Start.Result) > score);
    }

    /// <summary>&gt;0 gdy a lepszy od b: najpierw miejsce (niższe lepsze), potem wynik (wyższy lepszy).</summary>
    private static int Compare(StartResult a, StartResult b)
    {
        var pa = RankingBuilder.ParsePlace(a.Place);
        var pb = RankingBuilder.ParsePlace(b.Place);
        if (pa is not null && pb is not null) return pb.Value.CompareTo(pa.Value);
        var sa = RankingBuilder.ParseScore(a.Result);
        var sb = RankingBuilder.ParseScore(b.Result);
        if (sa is not null && sb is not null) return sa.Value.CompareTo(sb.Value);
        return 0;
    }
}
