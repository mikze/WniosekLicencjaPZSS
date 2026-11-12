using System.Globalization;
using System.Text.RegularExpressions;

namespace SzukajkaStartow;

/// <summary>
/// Przechodzi po liniach PDF-a, śledzi bieżącą konkurencję i nagłówek tabeli,
/// a gdy trafi na szukanego zawodnika, rozkłada wiersz na miejsce / klub / wynik / szczegóły.
/// Obsługuje typowe komunikaty klubowe ("Protokół nr X - ..."), protokoły wielobojowe
/// (kolumny psp/pcz/...), oraz wyniki ISSF z wynikiem w kilku wierszach ("REZULTATY").
/// </summary>
public static partial class ResultExtractor
{
    /// <summary>
    /// Nagłówek tabeli. Główna linia zawiera "Nazwisko"; linie tuż nad i pod nią (np. "Serie", "1 2 3")
    /// są pomocnicze i używane tylko wtedy, gdy wartość nie leży pod żadną kolumną głównej linii.
    /// </summary>
    private sealed class Header(IReadOnlyList<Cell> main, IReadOnlyList<Cell> extra)
    {
        public string? LabelFor(Cell value) =>
            Overlapping(main, value).MaxBy(h => Overlap(h, value))?.Text
            // Z linii pomocniczych bierzemy najwęższą pasującą etykietę ("3" zamiast szerokiego "Serie").
            ?? Overlapping(extra, value).MinBy(h => h.Right - h.Left)?.Text
            ?? Nearest(main, value);

        private static double Overlap(Cell a, Cell b) => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);

        private static IEnumerable<Cell> Overlapping(IReadOnlyList<Cell> cells, Cell value) =>
            cells.Where(h => Overlap(h, value) > 0);

        // Liczby wyrównane do prawej mogą nie nachodzić na nagłówek — dopuszczamy niewielką odległość.
        private static string? Nearest(IReadOnlyList<Cell> cells, Cell value)
        {
            var best = cells.MinBy(h => Math.Max(h.Left - value.Right, value.Left - h.Right));
            if (best is null) return null;
            var distance = Math.Max(best.Left - value.Right, value.Left - best.Right);
            return distance < 12 ? best.Text : null;
        }
    }

    public static (string? Title, List<StartResult> Results) Extract(
        List<TextLine> lines, IReadOnlyList<NameMatcher> matchers, PdfLink source)
    {
        var results = new List<StartResult>();
        string? title = null;
        string competition = "(nieznana konkurencja)";
        bool competitionFresh = false;   // konkurencja ustawiona po ostatnim nagłówku tabeli
        bool inJudgesSection = false;
        bool rezultatyPending = false, qualifierPending = false;
        string? shootingDate = null;
        Header? header = null;
        string? lastPlainLine = null;
        bool protocolTitlePending = false;
        var firstPageHeadings = new List<string>();
        string? documentDate = null;
        string? documentPlace = null;
        string? placeCandidate = null;
        bool headingsDone = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var text = line.Text.Trim();
            var norm = NameMatcher.RemoveDiacritics(text).ToUpperInvariant();

            // Tytuł dokumentu: linia po "KOMUNIKAT Z ZAWODÓW STRZELECKICH" (albo po samym "KOMUNIKAT").
            if (title is null && norm.StartsWith("KOMUNIKAT") && norm.Length <= 40 && i + 1 < lines.Count)
                title = lines[i + 1].Text.Trim();
            if (documentDate is null && DocumentDateRegex().Match(text) is { Success: true } dd)
            {
                documentDate = dd.Groups[2].Value.Trim();
                if (dd.Groups[1].Success) documentPlace = dd.Groups[1].Value;
            }

            // Zapasowo: samotna nazwa miejscowości na stronie tytułowej ("Złotoryja").
            if (placeCandidate is null && line.Page == 1 && line.Cells.Count == 1 && SingleWordRegex().IsMatch(text)
                && norm is not ("KOMUNIKAT" or "REZULTATY" or "WSTEPNE" or "PROTOKOL" or "WYNIKI" or "WNIOSEK"))
                placeCandidate = CultureInfo.GetCultureInfo("pl-PL").TextInfo.ToTitleCase(text.ToLower(CultureInfo.GetCultureInfo("pl-PL")));

            // Bez takiej linii: tytułem są nagłówki ze strony tytułowej.
            if (norm.StartsWith("SPIS TRESCI"))
                headingsDone = true; // dalej jest już spis treści, nie tytuł
            if (line.Page == 1 && !headingsDone && !IsBoilerplateHeading(norm))
                firstPageHeadings.Add(text);
            if (title is null && line.Page > 1 && firstPageHeadings.Count > 0)
                title = string.Join(" ", firstPageHeadings.Take(2));

            if (norm.StartsWith("OBSADA SEDZIOWSKA"))
            {
                inJudgesSection = true;
                header = null;
                lastPlainLine = null;
                continue;
            }

            if (protocolTitlePending)
            {
                protocolTitlePending = false;
                if (!IsNameHeader(norm) && !line.Cells.Any(c => IsNumeric(c.Text)) && !ShootingDateRegex().IsMatch(text))
                {
                    competition = text; // "Protokół nr 1" / "Match Results - Open"
                    continue;
                }
            }

            if (IpscTitleRegex().IsMatch(text))
            {
                SetCompetition(text); // IPSC: "Match Results - Open"
                continue;
            }

            var protocol = ProtocolRegex().Match(text);
            if (protocol.Success)
            {
                var name = protocol.Groups[1].Value.Trim();
                SetCompetition(name.Length > 0 ? name : text);
                protocolTitlePending = name.Length == 0;
                continue;
            }

            if (norm.EndsWith("REZULTATY") && norm.Length <= 30)
            {
                rezultatyPending = true;
                continue;
            }
            if (rezultatyPending)
            {
                rezultatyPending = false;
                if (!norm.StartsWith("SPIS"))
                {
                    SetCompetition(text);
                    qualifierPending = true;
                    continue;
                }
            }
            if (qualifierPending)
            {
                qualifierPending = false;
                if (QualifierRegex().IsMatch(norm))
                {
                    competition += " – " + text;
                    continue;
                }
            }

            var date = ShootingDateRegex().Match(text);
            if (date.Success)
            {
                shootingDate = date.Groups[1].Value.Trim();
                continue;
            }
            var issfDate = IssfDateRegex().Match(norm);
            if (issfDate.Success)
            {
                shootingDate = text[issfDate.Index..(issfDate.Index + issfDate.Length)].Trim();
                continue;
            }

            if (IsNameHeader(norm) && !line.Cells.Any(c => IsNumeric(c.Text)))
            {
                if (inJudgesSection && norm.Contains("FUNKCJA"))
                    continue; // nagłówek tabeli sędziów
                header = new Header(line.Cells, SubHeaderCells(lines, i));
                if (!competitionFresh)
                    competition = lastPlainLine ?? title ?? source.EventName ?? source.LinkText;
                inJudgesSection = false;
                competitionFresh = false;
                continue;
            }

            if (!line.Cells.Any(c => IsNumeric(c.Text)) && !norm.Contains("SEDZIA") && !PlaceDateRegex().IsMatch(norm))
                lastPlainLine = text;

            if (inJudgesSection) continue;

            foreach (var matcher in matchers)
            {
                var nameIdx = FindNameCell(line.Cells, matcher);
                if (nameIdx < 0) continue;
                // Podpisy sędziów itp. nie mają liczb — prawdziwy wiersz wyników ma.
                if (!line.Cells.Where((_, k) => k != nameIdx).Any(c => IsNumeric(c.Text) || IsStatus(c.Text)))
                    continue;

                var continuation = CollectContinuation(lines, i, line.Cells[nameIdx], header);
                results.Add(BuildResult(matcher, source, title, competition, shootingDate, header,
                    line, nameIdx, continuation) with { LineIndex = i });
            }
        }

        // Datę i tytuł znamy często dopiero po przeczytaniu całości — uzupełnij je w wynikach.
        return (title, results.Select(r => r with
        {
            DocumentTitle = r.DocumentTitle ?? title,
            DocumentDate = documentDate,
            DocumentPlace = documentPlace ?? placeCandidate,
        }).ToList());

        void SetCompetition(string name)
        {
            competition = name;
            competitionFresh = true;
            inJudgesSection = false;
            header = null;
            shootingDate = null;
        }
    }

    private static List<Cell> SubHeaderCells(List<TextLine> lines, int headerIndex)
    {
        var header = lines[headerIndex];
        var cells = new List<Cell>();
        foreach (var dir in new[] { -1, 1 })
            for (int j = headerIndex + dir; j >= 0 && j < lines.Count; j += dir)
            {
                var l = lines[j];
                if (l.Page != header.Page || Math.Abs(l.Y - header.Y) > 1.5 * Math.Max(l.Height, header.Height)) break;
                if (l.Cells.Any(c => c.Text.Length > 6)) break; // to już dane albo tytuł
                cells.AddRange(l.Cells);
            }
        return cells;
    }

    private static int FindNameCell(IReadOnlyList<Cell> cells, NameMatcher matcher)
    {
        for (int k = 0; k < cells.Count; k++)
            if (matcher.Matches(cells[k].Text)) return k;
        // Imię i nazwisko czasem trafiają do dwóch sąsiednich komórek.
        for (int k = 0; k + 1 < cells.Count; k++)
            if (matcher.Matches(cells[k].Text + " " + cells[k + 1].Text)) return k;
        return -1;
    }

    /// <summary>Kolejne linie tego samego zawodnika (np. "Leżąc", "Stojąc ... 577-28x" w wynikach ISSF).</summary>
    private static List<TextLine> CollectContinuation(List<TextLine> lines, int index, Cell nameCell, Header? header)
    {
        var result = new List<TextLine>();
        var prev = lines[index];
        for (int j = index + 1; j < lines.Count && result.Count < 6; j++)
        {
            var next = lines[j];
            if (next.Page != prev.Page) break;
            if (prev.Y - next.Y > 3.5 * Math.Max(prev.Height, next.Height)) break;
            if (IsNumeric(next.Cells[0].Text)) break;                  // kolejny zawodnik
            if (LeadingPlaceRegex().IsMatch(next.Cells[0].Text)) break; // kolejny zawodnik ("9 NOWAK, Jan")
            if (next.Left < nameCell.Left - 2) break;                   // tekst spoza tabeli
            if (next.Cells.Any(c => Math.Abs(c.Left - nameCell.Left) < 3 && c.Text.Any(char.IsLetter)))
                break;                                                  // inny zawodnik bez miejsca
            if (header is not null && next.Cells.Any(c => c.Text.Any(char.IsLetter) && header.LabelFor(c) is { } lbl
                    && IsNameHeader(NameMatcher.RemoveDiacritics(lbl).ToUpperInvariant())))
                break;                                                  // coś w kolumnie nazwisk = inny zawodnik
            var norm = NameMatcher.RemoveDiacritics(next.Text).ToUpperInvariant();
            if (norm.Contains("SEDZIA") || norm.Contains("KIEROWNIK") || norm.Contains("PROTOKOL")) break;
            result.Add(next);
            prev = next;
        }
        return result;
    }

    private static StartResult BuildResult(NameMatcher matcher, PdfLink source, string? title, string competition,
        string? shootingDate, Header? header, TextLine line, int nameIdx, List<TextLine> continuation)
    {
        string? place = null;
        string? result = null;
        var club = new List<string>();
        var details = new List<KeyValuePair<string, string>>();
        var nameCell = line.Cells[nameIdx];

        // Miejsce: komórka pod nagłówkiem "Miejsce"/"Msc"/"Lp", a jeśli go nie ma — pierwsza liczba przed nazwiskiem.
        for (int k = 0; k < nameIdx && place is null; k++)
            if (header?.LabelFor(line.Cells[k]) is { } placeLabel && PlaceLabelRegex().IsMatch(NameMatcher.RemoveDiacritics(placeLabel).ToUpperInvariant()))
                place = line.Cells[k].Text.TrimEnd('.');
        for (int k = 0; k < nameIdx && place is null; k++)
            if (IsNumeric(line.Cells[k].Text)) place = line.Cells[k].Text.TrimEnd('.');

        // Miejsce sklejone z nazwiskiem: "8 NOWAK, Jan".
        var athlete = nameCell.Text;
        var leading = LeadingPlaceRegex().Match(athlete);
        if (leading.Success)
        {
            place ??= leading.Groups[1].Value;
            athlete = leading.Groups[2].Value;
        }
        // Inny tekst sklejony przed nazwiskiem: "Mistrz SNAJPER 2025 NOWAK Jan".
        var words = athlete.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = matcher.FirstMatchingWord(words);
        if (firstName > 0)
        {
            place ??= string.Join(' ', words[..firstName]);
            athlete = string.Join(' ', words[firstName..]);
        }

        var valueCells = line.Cells.Skip(nameIdx + 1).Select(c => (c, first: true))
            .Concat(continuation.SelectMany(l => l.Cells.Where(c => c.Left > nameCell.Left - 2 || c.Right > nameCell.Right)
                .Select(c => (c, first: false))));

        foreach (var (cell, first) in valueCells)
        {
            var label = header?.LabelFor(cell);
            var labelNorm = label is null ? "" : NameMatcher.RemoveDiacritics(label).ToUpperInvariant();

            if (IsNameHeader(labelNorm))
                continue; // np. nazwisko rozbite na dwie komórki
            if (labelNorm.Contains("KLUB") || labelNorm.Contains("CLUB") || (header is null && first && !IsNumeric(cell.Text) && !IsStatus(cell.Text) && result is null && details.Count == 0))
            {
                club.Add(cell.Text);
                continue;
            }
            if (ResultLabelRegex().IsMatch(labelNorm) || (header is null && (IsNumeric(cell.Text) || IsStatus(cell.Text))))
            {
                result = cell.Text; // ostatnia wartość w kolumnie wyniku wygrywa (np. suma po seriach)
                if (header is not null) continue;
            }
            details.Add(new(label ?? "", cell.Text));
        }

        // Bez nagłówka ostatnia liczba jest wynikiem — nie powtarzaj jej w szczegółach.
        if (header is null && details.Count > 0 && details[^1].Value == result)
            details.RemoveAt(details.Count - 1);
        // Nagłówek bez kolumny "Wynik"/"Suma": wynikiem jest ostatnia liczba.
        if (result is null && details.LastOrDefault(d => IsNumeric(d.Value) || IsStatus(d.Value)) is { Value: not null } last)
        {
            result = last.Value;
            details.Remove(last);
        }

        // Przy DNS/DNF pierwsza liczba to numer zmiany/stanowiska, a nie lokata.
        if (result is not null && IsStatus(result))
            place = null;

        return new StartResult
        {
            Query = matcher.Query,
            Source = source,
            DocumentTitle = title,
            Competition = competition,
            ShootingDate = shootingDate,
            Place = place,
            Athlete = nameIdx + 1 < line.Cells.Count && header?.LabelFor(line.Cells[nameIdx + 1]) is { } l
                      && IsNameHeader(NameMatcher.RemoveDiacritics(l).ToUpperInvariant())
                ? athlete + " " + line.Cells[nameIdx + 1].Text
                : athlete,
            Club = club.Count > 0 ? string.Join(" ", club) : null,
            Result = result,
            Details = details,
            Page = line.Page,
            RawText = string.Join(" | ", new[] { line }.Concat(continuation).Select(l => l.Text)),
        };
    }

    private static bool IsBoilerplateHeading(string norm) =>
        norm.StartsWith("KLUB STRZELECKI") || norm.StartsWith("SPIS TRESCI") || norm is "ZLOTORYJA" || norm.StartsWith("KOMUNIKAT")
        || norm.EndsWith("REZULTATY") || PlaceDateRegex().IsMatch(norm) || !norm.Any(char.IsLetter)
        || norm.Count(char.IsDigit) > norm.Count(char.IsLetter);

    private static bool IsNameHeader(string norm)
    {
        var tokens = NameMatcher.Tokenize(norm);
        return tokens.Any(t => t is "NAZWISKO" or "NAZWISKA" or "NAME" or "PLACENAME" or "ZAWODNIK");
    }

    private static bool IsNumeric(string s) => NumericRegex().IsMatch(s.Trim());
    internal static bool IsStatus(string s) => StatusRegex().IsMatch(s.Trim());

    [GeneratedRegex(@"^Protok[oó]ł\s*nr\.?\s*\d+\s*[-–—:]?\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProtocolRegex();

    [GeneratedRegex(@"^(KWALIFIKACJE|ELIMINACJE|FINAL|POLFINAL|INDYWIDUALNE|DRUZYNOWE|DRUZYNY)\b")]
    private static partial Regex QualifierRegex();

    [GeneratedRegex(@"Data\s+strzelania\s*:?\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ShootingDateRegex();

    [GeneratedRegex(@"\b(PON|WT|SR|CZW|PT|SOB|NIEDZ|ND|NIE)\.?\s+\d{1,2}\s+\p{L}+\s+\d{4}")]
    private static partial Regex IssfDateRegex();

    [GeneratedRegex(@"^(WYNIK|SUMA|RAZEM|PKT\.?|PUNKTY|TOTAL|WYNIK KONCOWY|MATCH PTS|PTS|POINTS|SCORE)$")]
    private static partial Regex ResultLabelRegex();

    [GeneratedRegex(@"^\d+([.,]\d+)?\.?(\s*-\s*\d+x)?(\s+\d+([.,]\d+)?)*$", RegexOptions.IgnoreCase)]
    private static partial Regex NumericRegex();

    [GeneratedRegex(@"^(DNS|DNF|DSQ|DQ|NS|NK|DYSK)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"(?:(\p{Lu}[\p{L}-]+)\s*,?\s+)?\bdni(?:a|ach)\s+(\d{1,2}(?:\s*[-–]\s*\d{1,2})?\.\d{1,2}\.\d{4})")]
    private static partial Regex DocumentDateRegex();

    [GeneratedRegex(@"^\p{Lu}\p{L}{2,}$")]
    private static partial Regex SingleWordRegex();

    [GeneratedRegex(@"^Match Results\b", RegexOptions.IgnoreCase)]
    private static partial Regex IpscTitleRegex();

    [GeneratedRegex(@"^(\d+)\.?\s+(\p{L}.*)$")]
    private static partial Regex LeadingPlaceRegex();

    [GeneratedRegex(@"^(MIEJSCE|MSC|M-CE|LP|LP\.|PLACE|MSC ST|M)\b")]
    private static partial Regex PlaceLabelRegex();

    [GeneratedRegex(@"\bDNIA\b|^DATA\b|^\d+\s*$")]
    private static partial Regex PlaceDateRegex();
}
