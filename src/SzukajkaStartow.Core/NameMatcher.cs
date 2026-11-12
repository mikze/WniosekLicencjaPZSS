using System.Globalization;
using System.Text;

namespace SzukajkaStartow;

/// <summary>
/// Dopasowuje zawodnika niezależnie od kolejności imienia i nazwiska, wielkości liter,
/// przecinków i polskich znaków ("Łukasz Żak" == "ŻAK, Łukasz" == "ZAK LUKASZ").
/// </summary>
public sealed class NameMatcher
{
    public string Query { get; }
    private readonly string[] _tokens;

    public NameMatcher(string query)
    {
        Query = query.Trim();
        _tokens = Tokenize(Query);
        if (_tokens.Length == 0)
            throw new ArgumentException($"Puste zapytanie: '{query}'");
    }

    public bool Matches(string text)
    {
        var tokens = Tokenize(text);
        return _tokens.All(t => tokens.Contains(t));
    }

    /// <summary>Indeks pierwszego słowa, które jest częścią szukanego imienia/nazwiska (-1 gdy brak).</summary>
    public int FirstMatchingWord(string[] words)
    {
        for (int i = 0; i < words.Length; i++)
            if (Tokenize(words[i]).Any(t => _tokens.Contains(t))) return i;
        return -1;
    }

    public static string[] Tokenize(string text)
    {
        var normalized = RemoveDiacritics(text).ToUpperInvariant();
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
            sb.Append(char.IsLetter(c) ? c : ' ');
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string RemoveDiacritics(string text)
    {
        var decomposed = text.Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
