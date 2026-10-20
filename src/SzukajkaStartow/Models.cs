namespace SzukajkaStartow;

/// <summary>Link do PDF-a znaleziony na stronie z komunikatami.</summary>
public sealed record PdfLink(string Url, string? EventDate, string? EventName, string LinkText, string SourcePage);

/// <summary>Jeden start zawodnika znaleziony w PDF-ie.</summary>
public sealed record StartResult
{
    public required string Query { get; init; }
    public required PdfLink Source { get; init; }
    public string? DocumentTitle { get; init; }
    /// <summary>Data z treści PDF-a ("Złotoryja, dnia 14.02.2026 r.").</summary>
    public string? DocumentDate { get; init; }
    public string? Date => Source.EventDate ?? DocumentDate;
    /// <summary>Miejscowość z treści PDF-a ("Złotoryja, dnia ...").</summary>
    public string? DocumentPlace { get; init; }
    public required string Competition { get; init; }
    public string? ShootingDate { get; init; }
    public string? Place { get; init; }
    public required string Athlete { get; init; }
    public string? Club { get; init; }
    public string? Result { get; init; }
    /// <summary>Pozostałe kolumny (np. serie, składowe wieloboju) w kolejności z protokołu.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> Details { get; init; }
    public required int Page { get; init; }
    /// <summary>Numer linii w PDF-ie — do zachowania kolejności z protokołu.</summary>
    public int LineIndex { get; init; }
    public required string RawText { get; init; }
}
