using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace SzukajkaStartow;

[Flags]
public enum Discipline { None = 0, Pistol = 1, Rifle = 2, Shotgun = 4, All = Pistol | Rifle | Shotgun }

/// <summary>Jeden wiersz tabeli startów we wniosku. Note (np. "Pcz 10 strz.") jest tylko do podglądu w konsoli.</summary>
public sealed record LicenseRow(string Name, string Date, string? Place, Discipline Disciplines, string? Note)
{
    public bool Pistol => Disciplines.HasFlag(Discipline.Pistol);
    public bool Rifle => Disciplines.HasFlag(Discipline.Rifle);
    public bool Shotgun => Disciplines.HasFlag(Discipline.Shotgun);
}

/// <summary>Wiersze wniosku plus sprawdzenie wymagań PZSS (liczba startów w każdej dyscyplinie).</summary>
public sealed record LicensePlan(
    List<LicenseRow> Rows,
    Discipline Leading,
    Discipline Licensed,
    IReadOnlyDictionary<Discipline, int> Counts,
    IReadOnlyDictionary<Discipline, int> Required)
{
    /// <summary>4 starty w dyscyplinie wiodącej i po 2 w pozostałych dyscyplinach licencji.</summary>
    public bool MeetsRequirements => Required.All(r => Counts[r.Key] >= r.Value);
    public int Missing => Required.Sum(r => Math.Max(0, r.Value - Counts[r.Key]));
    public int LicensedStarts => Required.Keys.Sum(d => Counts[d]);

    /// <summary>
    /// Ustawiane, gdy zakres licencji wybrano automatycznie i zawodnik nie spełnia warunków dla wszystkich
    /// trzech dyscyplin — wtedy tu jest plan dla pełnej licencji (żeby pokazać, czego brakuje).
    /// </summary>
    public LicensePlan? FullLicensePlan { get; init; }
}

/// <summary>
/// Uzupełnia wniosek PZSS o przedłużenie licencji zawodniczej: nazwisko i imię, rok oraz tabelę startów.
/// Pozostałe pola (PESEL, nr licencji, podpis, kolumna "w kalendarzu WZSS/PZSS" itd.) zostają puste.
/// Wzór nie ma pól formularza, więc tekst jest nanoszony na stronę w miejscach wyznaczonych
/// na podstawie etykiet ze wzoru ("Nazwisko i imię", "1."–"8.", "w 20").
/// </summary>
public static partial class LicenseApplication
{
    public const int RowsPerPage = 8;

    // Granice kolumn tabeli we wzorze PZSS (punkty PDF, oś X).
    private static readonly (double Left, double Right) NameColumn = (85, 244);
    private static readonly (double Left, double Right) DateColumn = (244, 301);
    private static readonly (double Left, double Right) PlaceColumn = (301, 366);
    private static readonly (double Left, double Right) PistolColumn = (366, 383);
    private static readonly (double Left, double Right) RifleColumn = (383, 400);
    private static readonly (double Left, double Right) ShotgunColumn = (400, 416);
    private const double NameFieldLeft = 154;
    private const double NameFieldRight = 535;

    public static readonly Discipline[] AllDisciplines = [Discipline.Pistol, Discipline.Rifle, Discipline.Shotgun];

    // Wymagania PZSS: 4 starty w dyscyplinie wiodącej, po 2 w pozostałych.
    public const int LeadingMinimum = 4;
    public const int OtherMinimum = 2;

    /// <summary>Konkurencja, w której zawodnik startował, z jej dyscypliną (Snajper może mieć kilka).</summary>
    private sealed record Start(StartResult Result, Discipline Discipline);

    private sealed class WorkRow
    {
        public required string Name { get; init; }
        public required string Date { get; init; }
        public string? Place { get; init; }
        public required List<Start> Starts { get; init; }
        /// <summary>Dyscypliny, dla których zawody rozbito na osobne wiersze — ich konkurencje idą do "Uwag".</summary>
        public Discipline SplitFor { get; set; }
        public Discipline Disciplines => Starts.Aggregate(Discipline.None, (acc, s) => acc | s.Discipline);
    }

    /// <summary>
    /// Zamienia starty na wiersze wniosku: jedne zawody = jeden wiersz. Jeśli w którejś dyscyplinie brakuje
    /// startów do minimum PZSS, zawody z kilkoma konkurencjami tej dyscypliny są rozbijane na osobne wiersze
    /// (np. Psp i Pcz na tych samych zawodach = 2 starty pistoletowe), a konkurencja trafia do kolumny "Uwagi".
    /// </summary>
    /// <param name="leading">Dyscyplina wiodąca; null = wybierz automatycznie tę, przy której warunki są spełnione.</param>
    /// <param name="licensed">Dyscypliny objęte licencją; null = sprawdź pełną licencję, a jeśli warunki
    /// nie są spełnione — największy zakres licencji (np. sam karabin), dla którego są.</param>
    public static LicensePlan BuildRows(IEnumerable<StartResult> results, int year,
        Discipline? leading = null, Discipline? licensed = null)
    {
        var list = results.ToList();
        if (licensed is { } lic)
            return BestPlan(list, year, lic | (leading ?? Discipline.None), leading);

        var full = BestPlan(list, year, Discipline.All, leading);
        if (full.MeetsRequirements) return full;

        // Zawodnik może mieć licencję tylko na część dyscyplin (np. sam karabin) — wtedy pozostałe go nie dotyczą.
        Discipline[] subsets =
        [
            Discipline.Pistol | Discipline.Rifle, Discipline.Pistol | Discipline.Shotgun, Discipline.Rifle | Discipline.Shotgun,
            Discipline.Pistol, Discipline.Rifle, Discipline.Shotgun,
        ];
        var narrowed = subsets
            .Where(sub => leading is null || sub.HasFlag(leading.Value))
            .Select(sub => BestPlan(list, year, sub, leading))
            .Where(p => p.MeetsRequirements)
            .OrderByDescending(p => p.Required.Count)
            .ThenByDescending(p => p.LicensedStarts)
            .FirstOrDefault();
        return narrowed is null ? full : narrowed with { FullLicensePlan = full };
    }

    private static LicensePlan BestPlan(List<StartResult> results, int year, Discipline licensed, Discipline? leading)
    {
        var candidates = leading is { } l ? [l] : AllDisciplines.Where(d => licensed.HasFlag(d)).ToArray();
        var plans = candidates.Select(d => BuildRows(results, year, d, licensed)).ToList();
        return plans.FirstOrDefault(p => p.MeetsRequirements)
               // Nikt nie spełnia: najmniej brakujących startów, przy remisie dyscyplina, w której startów jest najwięcej.
               ?? plans.OrderBy(p => p.Missing).ThenByDescending(p => p.Counts[p.Leading]).First();
    }

    private static LicensePlan BuildRows(List<StartResult> results, int year, Discipline leading, Discipline licensed)
    {
        var started = results
            .Where(r => r.Result is null || !ResultExtractor.IsStatus(r.Result) || r.Result.Trim().ToUpperInvariant() is "DNF")
            .Where(r => YearOf(r.Date) == year)
            .ToList();

        // Zawody z kilkoma komunikatami (zawody + konsultacje) to jeden wiersz tabeli strony.
        var groups = started.GroupBy(r => r.Source.EventName is null ? r.Source.Url : r.Source.EventName + "|" + r.Date);

        var knownPlaces = started.Select(r => r.DocumentPlace).Where(p => p is not null).ToList();
        var defaultPlace = knownPlaces.GroupBy(p => p).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;

        var rows = new List<WorkRow>();
        foreach (var g in groups)
        {
            // Tytuł z PDF-a zaczynający się małą literą to urwany drugi wiersz ("z okazji ...") — wtedy nazwa ze strony.
            var titles = g.Select(r => r.DocumentTitle)
                .Where(t => !string.IsNullOrWhiteSpace(t) && !char.IsLower(t![0]))
                .Distinct().ToList();
            var name = titles.FirstOrDefault(t => !t!.StartsWith("Konsultacje", StringComparison.OrdinalIgnoreCase))
                       ?? titles.FirstOrDefault()
                       ?? g.First().Source.EventName
                       ?? g.First().Source.LinkText;

            rows.Add(new WorkRow
            {
                Name = name!,
                Date = CleanDate(g.First().Date),
                Place = g.Select(r => r.DocumentPlace).FirstOrDefault(p => p is not null) ?? defaultPlace,
                Starts = g.Select(r => new Start(r, Disciplines(r))).Where(s => s.Discipline != Discipline.None).ToList(),
            });
        }
        rows.RemoveAll(r => r.Starts.Count == 0);

        // Wymagania dotyczą tylko dyscyplin z licencji: 4 w wiodącej, po 2 w pozostałych.
        var required = AllDisciplines.Where(d => licensed.HasFlag(d))
            .ToDictionary(d => d, d => d == leading ? LeadingMinimum : OtherMinimum);
        // Najpierw uzupełniamy dyscyplinę wiodącą, potem pozostałe.
        var order = required.Keys.OrderBy(d => d == leading ? 0 : 1).ToList();
        while (SplitOnce(rows, order, required)) { }

        var plan = rows.Select(r => new LicenseRow(r.Name, r.Date, r.Place, r.Disciplines, Note(r))).ToList();
        return new LicensePlan(plan, leading, licensed, CountRows(rows), required);
    }

    /// <summary>Wydziela jedną konkurencję w osobny wiersz tam, gdzie brakuje startów. False = nic się nie da zrobić.</summary>
    private static bool SplitOnce(List<WorkRow> rows, List<Discipline> order, Dictionary<Discipline, int> required)
    {
        var counts = CountRows(rows);
        foreach (var d in order.Where(d => counts[d] < required[d]))
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var inDiscipline = row.Starts.Where(s => s.Discipline.HasFlag(d)).ToList();
                if (inDiscipline.Count < 2) continue;

                // Pierwsza konkurencja zostaje w wierszu zawodów; wydzielamy kolejną,
                // najlepiej "czystą" (tylko ta dyscyplina, a nie np. wielobój).
                var peel = inDiscipline.Skip(1).OrderBy(s => s.Discipline == d ? 0 : 1).First();
                row.Starts.Remove(peel);
                row.SplitFor |= d;
                rows.Insert(i + 1, new WorkRow
                {
                    Name = row.Name, Date = row.Date, Place = row.Place,
                    Starts = [peel], SplitFor = d,
                });
                return true;
            }
        }
        return false;
    }

    private static Dictionary<Discipline, int> CountRows(List<WorkRow> rows) =>
        AllDisciplines.ToDictionary(d => d, d => rows.Count(r => r.Disciplines.HasFlag(d)));

    private static string? Note(WorkRow row)
    {
        if (row.SplitFor == Discipline.None) return null;
        var names = row.Starts.Where(s => (s.Discipline & row.SplitFor) != 0)
            .Select(s => Abbreviate(s.Result.Competition)).Distinct().ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    /// <summary>"Pistolet centralnego zapłonu 10 strzałów" → "Pcz 10 strz." (do wąskiej kolumny "Uwagi").</summary>
    public static string Abbreviate(string competition)
    {
        var s = competition;
        foreach (var (pattern, replacement) in Abbreviations)
            s = Regex.Replace(s, pattern, replacement, RegexOptions.IgnoreCase);
        return WhitespaceRegex().Replace(s, " ").Trim();
    }

    private static readonly (string, string)[] Abbreviations =
    [
        (@"pistolet\s+sportowy", "Psp"),
        (@"pistolet\s+centralnego\s+zap[łl]onu", "Pcz"),
        (@"pistolet\s+pneumatyczny", "Ppn"),
        (@"karabin\s+sportowy", "Ksp"),
        (@"karabin\s+centralnego\s+zap[łl]onu", "Kcz"),
        (@"karabin\s+pneumatyczny", "Kpn"),
        (@"karabin\s+dowolny", "Kdw"),
        (@"strzelba(\s+g[łl]adkolufowa)?", "Strzelba"),
        (@"przyrz[ąa]dy\s+mechaniczne", "mech."),
        (@"strza[łl](ów|y)", "strz."),
    ];

    /// <summary>Nazwisko i imię w formie do wniosku, np. "NOWAK JAN" (najczęstszy zapis w protokołach).</summary>
    public static string AthleteName(IEnumerable<StartResult> results, string fallback)
    {
        var name = results
            .Select(r => WhitespaceRegex().Replace(r.Athlete.Replace(",", " "), " ").Trim())
            .GroupBy(n => n.ToUpperInvariant())
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;
        return name ?? fallback.ToUpperInvariant();
    }

    public static void Fill(string templatePath, string fontPath, string outputPath,
        string athleteName, int year, IReadOnlyList<LicenseRow> rows)
    {
        var templateBytes = File.ReadAllBytes(templatePath);
        var layout = FindLayout(templateBytes);

        using var template = PdfDocument.Open(templateBytes);
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(File.ReadAllBytes(fontPath));

        var chunks = rows.Count == 0 ? [[]] : rows.Chunk(RowsPerPage).ToList();
        foreach (var chunk in chunks)
        {
            var page = builder.AddPage(template, 1);
            page.NewContentStreamAfter();
            page.SetTextAndFillColor(0, 0, 0);

            WriteFitted(page, font, athleteName, NameFieldLeft, NameFieldRight, layout.NameBaseline, 11);
            page.AddText((year % 100).ToString("00"), 11, new PdfPoint(layout.YearX + 5, layout.YearBaseline), font);

            for (int i = 0; i < chunk.Length; i++)
            {
                var row = chunk[i];
                var y = layout.RowBaselines[i];
                WriteFitted(page, font, row.Name, NameColumn.Left + 3, NameColumn.Right - 3, y, 9);
                WriteFitted(page, font, row.Date, DateColumn.Left + 2, DateColumn.Right - 2, y, 9);
                if (row.Place is not null)
                    WriteFitted(page, font, row.Place, PlaceColumn.Left + 2, PlaceColumn.Right - 2, y, 9);
                if (row.Pistol) WriteCentered(page, font, "X", PistolColumn, y, 10);
                if (row.Rifle) WriteCentered(page, font, "X", RifleColumn, y, 10);
                if (row.Shotgun) WriteCentered(page, font, "X", ShotgunColumn, y, 10);
            }
        }

        File.WriteAllBytes(outputPath, builder.Build());
    }

    private sealed record Layout(double NameBaseline, double YearX, double YearBaseline, double[] RowBaselines);

    /// <summary>Wyznacza pozycje pól na podstawie etykiet ze wzoru — sprawdza też, czy to właściwy wzór.</summary>
    private static Layout FindLayout(byte[] template)
    {
        var lines = PdfLayout.ReadLines(template).Where(l => l.Page == 1).ToList();
        var cells = lines.SelectMany(l => l.Cells.Select(c => (Line: l, Cell: c))).ToList();

        var nameLabel = cells.FirstOrDefault(x => x.Cell.Text.StartsWith("Nazwisko i imi", StringComparison.OrdinalIgnoreCase));
        var yearLabel = cells.FirstOrDefault(x => x.Cell.Text.Replace(" ", "") == "w20");
        var rowLabels = Enumerable.Range(1, RowsPerPage)
            .Select(n => cells.FirstOrDefault(x => x.Cell.Text == $"{n}." && x.Cell.Left < NameColumn.Left))
            .ToList();

        if (nameLabel.Line is null || yearLabel.Line is null || rowLabels.Any(r => r.Line is null))
            throw new InvalidOperationException(
                "Wzór wniosku ma inny układ niż oczekiwany (brak etykiet 'Nazwisko i imię', 'w 20' lub wierszy 1.–8.).");

        return new Layout(
            nameLabel.Line.Y,
            yearLabel.Cell.Right,
            yearLabel.Line.Y,
            rowLabels.Select(r => r.Line.Y).ToArray());
    }

    /// <summary>Wpisuje tekst w pole; jeśli się nie mieści, zmniejsza czcionkę, a w ostateczności skraca.</summary>
    private static void WriteFitted(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text,
        double left, double right, double baseline, double size)
    {
        var width = right - left;
        const double minSize = 6;
        while (size > minSize && Width(page, font, text, size) > width)
            size -= 0.5;
        while (text.Length > 1 && Width(page, font, text, size) > width)
            text = text[..^2].TrimEnd() + "…";
        page.AddText(text, size, new PdfPoint(left, baseline), font);
    }

    private static void WriteCentered(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text,
        (double Left, double Right) column, double baseline, double size)
    {
        var w = Width(page, font, text, size);
        page.AddText(text, size, new PdfPoint((column.Left + column.Right - w) / 2, baseline), font);
    }

    private static double Width(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, double size)
    {
        var letters = page.MeasureText(text, size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters.Max(l => l.GlyphRectangle.Right) - letters.Min(l => l.GlyphRectangle.Left);
    }

    private static Discipline Disciplines(StartResult r)
    {
        // Nazwa konkurencji, a dla wielobojów także kolumny, w których zawodnik ma wynik (psp, ksp, STRZELBA...).
        var tokens = NameMatcher.Tokenize(r.Competition)
            .Concat(r.Details.SelectMany(d => NameMatcher.Tokenize(d.Key)))
            .ToList();
        var result = Classify(tokens);
        if (result == Discipline.None)
        {
            // Np. IPSC ("Match Results - Open") — dyscyplina wynika z tytułu dokumentu lub nazwy zawodów.
            var context = $"{r.DocumentTitle} {r.Source.EventName} {r.Source.LinkText}";
            result = Classify(NameMatcher.Tokenize(context));
            if (result == Discipline.None && Regex.IsMatch(context, "IPSC|dynami", RegexOptions.IgnoreCase))
                result = Discipline.Pistol;
        }
        return result;
    }

    private static Discipline Classify(IEnumerable<string> tokens)
    {
        var d = Discipline.None;
        foreach (var t in tokens)
        {
            if (t.StartsWith("PISTOLET") || t is "PSP" or "PCZ" or "PPN" or "PST") d |= Discipline.Pistol;
            if (t.StartsWith("KARABIN") || t is "KSP" or "KCZ" or "KDW" or "KPN" or "PCC") d |= Discipline.Rifle;
            if (t.StartsWith("STRZELB")) d |= Discipline.Shotgun;
        }
        return d;
    }

    private static int? YearOf(string? date) =>
        date is not null && YearRegex().Matches(date) is { Count: > 0 } m ? int.Parse(m[^1].Value) : null;

    private static string CleanDate(string? date) =>
        date is null ? "" : TrailingRRegex().Replace(date.Trim(), "");

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\s*r\.?$")]
    private static partial Regex TrailingRRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
