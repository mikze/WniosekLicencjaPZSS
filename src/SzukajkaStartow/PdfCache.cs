using System.Text.RegularExpressions;

namespace SzukajkaStartow;

/// <summary>Pobiera PDF-y i trzyma je lokalnie, żeby kolejne wyszukiwania nie ściągały wszystkiego od nowa.</summary>
public sealed partial class PdfCache(HttpClient http, string directory, bool refresh)
{
    public async Task<byte[]> GetAsync(string url, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var uri = new Uri(url);
        var fileName = UnsafeChars().Replace(uri.Host + uri.AbsolutePath, "_");
        var path = Path.Combine(directory, fileName);

        if (!refresh && File.Exists(path))
            return await File.ReadAllBytesAsync(path, ct);

        var bytes = await http.GetByteArrayAsync(url, ct);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, bytes, ct);
        File.Move(tmp, path, overwrite: true);
        return bytes;
    }

    [GeneratedRegex(@"[^A-Za-z0-9._-]+")]
    private static partial Regex UnsafeChars();
}
