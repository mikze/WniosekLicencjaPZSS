using System.Collections.Concurrent;
using System.Text;
using SzukajkaStartow;

Console.OutputEncoding = Encoding.UTF8;

const string DefaultBase = "https://agat.zlotoryja.pl/komunikaty/komunikaty-{0}/";

var names = new List<string>();
var pages = new List<string>();
string? csvPath = null;
bool allYears = false, refresh = false, verbose = false;
string? applicationPath = null;
int? applicationYear = null;
Discipline? leading = null;  // null = wybór automatyczny
Discipline? licensed = null; // null = sprawdź pełną licencję, a gdy się nie da — największy pasujący zakres
string assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
string templatePath = Path.Combine(assetsDir, "wniosek-o-przedluzenie-licencji-zawodniczej.pdf");
string fontPath = Path.Combine(assetsDir, "LiberationSans-Regular.ttf");
string cacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SzukajkaStartow", "cache");

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Brak wartości dla {args[i]}");
    switch (args[i])
    {
        case "-h" or "--help" or "--pomoc":
            PrintHelp();
            return 0;
        case "-u" or "--url": pages.Add(Next()); break;
        case "-r" or "--rok": pages.Add(string.Format(DefaultBase, Next())); break;
        case "-a" or "--wszystkie-lata": allYears = true; break;
        case "-l" or "--lista":
            names.AddRange(File.ReadAllLines(Next()).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')));
            break;
        case "--csv": csvPath = Next(); break;
        case "--cache": cacheDir = Next(); break;
        case "--odswiez": refresh = true; break;
        case "-v" or "--szczegoly": verbose = true; break;
        case "-w" or "--wniosek": applicationPath = Next(); break;
        case "--rok-wniosku": applicationYear = int.Parse(Next()); break;
        case "--wiodaca": leading = ParseDiscipline(Next()); break;
        case "--licencja":
            licensed = Next().Split([',', '+', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Aggregate(Discipline.None, (acc, x) => acc | ParseDiscipline(x));
            break;
        case "--wzor": templatePath = Next(); break;
        case "--czcionka": fontPath = Next(); break;
        case "--zrzut":
            // Diagnostyka: jak program widzi wiersze i kolumny w danym PDF-ie (plik lokalny lub URL).
            var src = Next();
            byte[] pdf;
            if (File.Exists(src)) pdf = File.ReadAllBytes(src);
            else { using var h = new HttpClient(); pdf = await h.GetByteArrayAsync(src); }
            foreach (var l in PdfLayout.ReadLines(pdf))
                Console.WriteLine($"[s{l.Page} y{l.Y,6:F1}] " + string.Join(" | ", l.Cells.Select(c => $"{c.Text}@{c.Left:F0}-{c.Right:F0}")));
            return 0;
        default:
            if (args[i].StartsWith('-')) { Console.Error.WriteLine($"Nieznana opcja: {args[i]}"); return 2; }
            names.Add(args[i]);
            break;
    }
}

if (names.Count == 0)
{
    PrintHelp();
    return 2;
}
applicationYear ??= DateTime.Now.Year;
if (pages.Count == 0)
    pages.Add(string.Format(DefaultBase, applicationPath is null ? DateTime.Now.Year : applicationYear));

var matchers = names.Select(n => new NameMatcher(n)).ToList();
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("SzukajkaStartow/1.0");
var scraper = new WebScraper(http);
var cache = new PdfCache(http, cacheDir, refresh);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

if (allYears)
{
    var expanded = new List<string>();
    foreach (var p in pages)
        expanded.AddRange(await scraper.GetYearPagesAsync(p, cts.Token));
    pages = expanded.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

var links = new List<PdfLink>();
foreach (var page in pages)
{
    try
    {
        var found = await scraper.GetPdfLinksAsync(page, cts.Token);
        Console.Error.WriteLine($"{page}: {found.Count} PDF");
        links.AddRange(found);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine($"Nie udało się pobrać strony {page}: {ex.Message}");
    }
}

var all = new ConcurrentBag<StartResult>();
var warnings = new ConcurrentBag<string>();
int done = 0;
await Parallel.ForEachAsync(links, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cts.Token },
    async (link, ct) =>
    {
        try
        {
            var bytes = await cache.GetAsync(link.Url, ct);
            var lines = PdfLayout.ReadLines(bytes);
            if (lines.Count == 0)
                warnings.Add($"Brak tekstu (skan?): {link.Url}");
            var (_, results) = ResultExtractor.Extract(lines, matchers, link);
            foreach (var r in results) all.Add(r);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Błąd {link.Url}: {ex.Message}");
        }
        var n = Interlocked.Increment(ref done);
        Console.Error.Write($"\rPrzetworzono {n}/{links.Count} plików...");
    });
Console.Error.WriteLine();
foreach (var w in warnings.OrderBy(w => w)) Console.Error.WriteLine("UWAGA: " + w);

var ordered = all
    .OrderBy(r => names.IndexOf(r.Query))
    .ThenBy(r => links.IndexOf(r.Source))
    .ThenBy(r => r.LineIndex)
    .ToList();

Report.PrintConsole(ordered, matchers.Select(m => m.Query).ToList(), verbose);
if (csvPath is not null)
{
    Report.WriteCsv(ordered, csvPath);
    Console.Error.WriteLine($"Zapisano CSV: {Path.GetFullPath(csvPath)}");
}

if (applicationPath is not null)
{
    foreach (var query in names)
    {
        var mine = ordered.Where(r => r.Query == query).ToList();
        var plan = LicenseApplication.BuildRows(mine, applicationYear.Value, leading, licensed);
        var rows = plan.Rows;
        var athlete = LicenseApplication.AthleteName(mine, query);
        // Przy kilku zawodnikach każdy dostaje osobny plik: wniosek-NOWAK_JAN.pdf
        var path = names.Count == 1 ? applicationPath : Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(applicationPath))!,
            $"{Path.GetFileNameWithoutExtension(applicationPath)}-{string.Join('_', NameMatcher.Tokenize(athlete))}.pdf");

        LicenseApplication.Fill(templatePath, fontPath, path, athlete, applicationYear.Value, rows);

        Console.WriteLine();
        Console.WriteLine($"Wniosek {applicationYear} dla {athlete}: {Path.GetFullPath(path)}");
        if (rows.Count == 0)
            Console.WriteLine($"  (brak startów w {applicationYear} r. — wpisano tylko nazwisko i rok)");
        foreach (var (row, n) in rows.Select((r, n) => (r, n + 1)))
            Console.WriteLine($"  {n,2}. {row.Name} | {row.Date} | {row.Place ?? "?"} | " +
                $"{(row.Pistol ? "P" : "-")}{(row.Rifle ? "K" : "-")}{(row.Shotgun ? "S" : "-")}" +
                (row.Note is null ? "" : $" | {row.Note}"));
        PrintVerdict(plan, licensed is null);
        if (rows.Count > LicenseApplication.RowsPerPage)
            Console.WriteLine($"  Uwaga: {rows.Count} zawodów — wniosek ma {Math.Ceiling(rows.Count / (double)LicenseApplication.RowsPerPage)} strony " +
                $"(po {LicenseApplication.RowsPerPage} wierszy). Usuń zbędne, jeśli wystarczy jedna.");
    }
}
return 0;

// Bilans startów i werdykt: czy zawodnik spełnia warunki PZSS.
static void PrintVerdict(LicensePlan plan, bool autoLicense)
{
    Console.WriteLine("  Starty: " + string.Join(", ", LicenseApplication.AllDisciplines.Select(d =>
        plan.Required.TryGetValue(d, out var req)
            ? $"{DisciplineName(d)}{(d == plan.Leading ? " (wiodąca)" : "")} {plan.Counts[d]}/{req} {(plan.Counts[d] >= req ? "✓" : "✗")}"
            : $"{DisciplineName(d)} {plan.Counts[d]} (poza licencją)")));

    var scope = LicenseName(plan.Licensed);
    var rule = plan.Required.Count == 1
        ? $"4 starty: {DisciplineName(plan.Leading)}"
        : $"4 starty w wiodącej: {DisciplineName(plan.Leading)}, po 2 w pozostałych";

    if (plan.MeetsRequirements)
    {
        Console.WriteLine($"  ✅ SPEŁNIA warunki PZSS dla licencji: {scope} ({rule}).");
        if (plan.FullLicensePlan is { } full)
            Console.WriteLine($"     Dla licencji na wszystkie dyscypliny brakuje startów: {MissingText(full)}.");
    }
    else
    {
        Console.WriteLine($"  ❌ NIE SPEŁNIA warunków PZSS dla licencji: {scope} — brakuje startów: {MissingText(plan)} " +
            $"(najbliżej przy wiodącej: {DisciplineName(plan.Leading)}).");
        if (autoLicense)
            Console.WriteLine("     Jeśli licencja obejmuje tylko część dyscyplin, podaj ją: --licencja karabin (albo np. pistolet,karabin).");
    }
}

static string MissingText(LicensePlan plan) => string.Join(", ", plan.Required
    .Where(r => plan.Counts[r.Key] < r.Value)
    .Select(r => $"{DisciplineName(r.Key)} {r.Value - plan.Counts[r.Key]}"));

static string LicenseName(Discipline licensed) => licensed == Discipline.All
    ? "pistolet + karabin + strzelba"
    : string.Join(" + ", LicenseApplication.AllDisciplines.Where(d => licensed.HasFlag(d)).Select(DisciplineName));

static Discipline ParseDiscipline(string s) => s.Trim().ToLowerInvariant() switch
{
    "pistolet" or "p" => Discipline.Pistol,
    "karabin" or "k" => Discipline.Rifle,
    "strzelba" or "s" => Discipline.Shotgun,
    var x => throw new ArgumentException($"Nieznana dyscyplina: {x} (pistolet/karabin/strzelba)"),
};

static string DisciplineName(Discipline d) => d switch
{
    Discipline.Pistol => "pistolet",
    Discipline.Rifle => "karabin",
    _ => "strzelba",
};

static void PrintHelp() => Console.WriteLine("""
    SzukajkaStartow — wyszukuje zawodników w komunikatach PDF z zawodów strzeleckich.

    Użycie:
      SzukajkaStartow [opcje] "Imię Nazwisko" ["Imię Nazwisko" ...]

    Kolejność imienia i nazwiska, wielkość liter i polskie znaki nie mają znaczenia.
    Samo nazwisko też zadziała (znajdzie wszystkich o tym nazwisku).

    Opcje:
      -u, --url <adres>        strona z komunikatami (można podać kilka razy)
      -r, --rok <RRRR>         skrót: https://agat.zlotoryja.pl/komunikaty/komunikaty-RRRR/
      -a, --wszystkie-lata     przejrzyj też strony z innych lat podlinkowane na stronie
      -l, --lista <plik>       plik z listą zawodników (jeden na linię)
          --csv <plik>         zapisz wyniki do CSV (separator ';', otwiera się w Excelu)
          --cache <katalog>    gdzie trzymać pobrane PDF-y
          --odswiez            pobierz PDF-y ponownie, ignorując cache
      -v, --szczegoly          pokaż serie/składowe i surowy wiersz z PDF-a

    Wniosek o przedłużenie licencji (PZSS):
      -w, --wniosek <plik.pdf> uzupełnij wniosek: nazwisko i imię, rok i tabela startów
                               (przy kilku zawodnikach powstaje osobny plik dla każdego)
          --rok-wniosku <RRRR> rok, z którego brać starty (domyślnie bieżący)
          --licencja <lista>   dyscypliny objęte licencją, np. karabin albo pistolet,karabin
                               (domyślnie: wszystkie; gdy warunki nie są spełnione, program sam
                               sprawdzi, dla jakiego zakresu licencji zawodnik je spełnia)
          --wiodaca <dyscyplina> pistolet / karabin / strzelba — wymuś dyscyplinę wiodącą.
                               Domyślnie program sam wybiera tę, przy której spełnione są warunki
                               PZSS (4 starty w wiodącej, po 2 w pozostałych); gdy brakuje startów,
                               konkurencje z tych samych zawodów są rozpisywane w osobnych wierszach
          --wzor <plik.pdf>    inny plik wzoru wniosku (ten sam układ)
          --czcionka <plik.ttf> inna czcionka TrueType z polskimi znakami
      -h, --help               ta pomoc

    Domyślnie przeglądana jest strona z bieżącego roku.

    Przykłady:
      SzukajkaStartow "Jan Nowak"
      SzukajkaStartow -r 2025 -r 2026 "nowak jan" "Kowalska Anna" --csv wyniki.csv
      SzukajkaStartow -a -l zawodnicy.txt
      SzukajkaStartow "Jan Nowak" --wniosek wniosek.pdf --rok-wniosku 2025
    """);
