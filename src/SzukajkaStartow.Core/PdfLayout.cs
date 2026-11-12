using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SzukajkaStartow;

/// <summary>Fragment wiersza oddzielony od sąsiadów wyraźną przerwą — w praktyce komórka tabeli.</summary>
public sealed record Cell(string Text, double Left, double Right)
{
    public double Center => (Left + Right) / 2;
}

public sealed record TextLine(int Page, double Y, double Height, IReadOnlyList<Cell> Cells)
{
    public string Text => string.Join("  ", Cells.Select(c => c.Text));
    public double Left => Cells[0].Left;
}

/// <summary>
/// Odtwarza wiersze i kolumny z PDF-a: grupuje słowa w linie po współrzędnej Y,
/// a w obrębie linii dzieli je na komórki tam, gdzie odstęp jest większy niż zwykła spacja.
/// </summary>
public static class PdfLayout
{
    public static List<TextLine> ReadLines(byte[] pdf)
    {
        var lines = new List<TextLine>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
            lines.AddRange(ReadPage(page));
        return lines;
    }

    private static IEnumerable<TextLine> ReadPage(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => CenterY(w))
            .ToList();

        var groups = new List<List<Word>>();
        foreach (var w in words)
        {
            var last = groups.Count > 0 ? groups[^1] : null;
            if (last is not null)
            {
                var lineY = last.Average(CenterY);
                var tolerance = Math.Max(1.5, 0.35 * Math.Max(Height(w), last.Max(Height)));
                if (Math.Abs(CenterY(w) - lineY) <= tolerance)
                {
                    last.Add(w);
                    continue;
                }
            }
            groups.Add([w]);
        }

        foreach (var g in groups)
        {
            var sorted = g.OrderBy(w => w.BoundingBox.Left).ToList();
            yield return new TextLine(page.Number, g.Average(CenterY), g.Average(Height), SplitCells(sorted));
        }
    }

    private static List<Cell> SplitCells(List<Word> words)
    {
        var cells = new List<Cell>();
        var current = new List<Word> { words[0] };
        for (int i = 1; i < words.Count; i++)
        {
            var prev = words[i - 1];
            var w = words[i];
            var gap = w.BoundingBox.Left - prev.BoundingBox.Right;
            var charWidth = Math.Max(AvgCharWidth(prev), AvgCharWidth(w));
            // Zwykła spacja to ok. 0,5–0,7 szerokości znaku; większa przerwa = nowa kolumna.
            // Dwie liczby obok siebie (np. kolejne serie) rozdzielamy już przy mniejszej przerwie.
            var threshold = IsNumber(prev.Text) && IsNumber(w.Text) ? 0.8 : 1.6;
            if (gap > threshold * charWidth)
            {
                cells.Add(ToCell(current));
                current = [];
            }
            current.Add(w);
        }
        cells.Add(ToCell(current));
        return cells;
    }

    private static bool IsNumber(string s) =>
        s.Length > 0 && char.IsDigit(s[0]) && s.All(c => char.IsDigit(c) || c is '.' or ',' or '-' or 'x' or 'X');

    private static Cell ToCell(List<Word> words) => new(
        string.Join(" ", words.Select(w => w.Text)),
        words.Min(w => w.BoundingBox.Left),
        words.Max(w => w.BoundingBox.Right));

    private static double AvgCharWidth(Word w) =>
        w.Text.Length == 0 ? Height(w) * 0.5 : Math.Max(w.BoundingBox.Width / w.Text.Length, Height(w) * 0.3);

    // Linia bazowa jest wspólna dla całego wiersza, niezależnie od kształtu liter (".", "Ł", "g").
    private static double CenterY(Word w) => w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom;
    private static double Height(Word w) => Math.Max(w.BoundingBox.Height, 1);
}
