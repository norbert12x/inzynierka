namespace Inżynierka.Services;

// Lokalny katalog plików zdjęć: <katalog rozwiązania>/.local/photo-cache (ignorowany przez Git).
// cached_relative_path w bazie jest względna do katalogu rozwiązania, np. ".local/photo-cache/<plik>.jpg".
public static class PhotoCache
{
    public static string Root(string contentRootPath)
        => Path.GetFullPath(Path.Combine(contentRootPath, "..", ".local", "photo-cache"));

    // Pełna ścieżka pliku; null, gdy ścieżka z bazy wychodzi poza katalog photo-cache.
    public static string? ResolveFile(string contentRootPath, string cachedRelativePath)
    {
        var root = Root(contentRootPath);
        var path = Path.GetFullPath(Path.Combine(contentRootPath, "..", cachedRelativePath));
        return path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    // Obsługiwane formaty według rozszerzenia; null = nieobsługiwany.
    public static string? ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null
    };
}
