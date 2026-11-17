using Inżynierka.Services;

namespace Inżynierka.Tests;

public class PhotoCacheTests
{
    // Katalog projektu; pliki zdjęć są w <rozwiązanie>/.local/photo-cache.
    private static readonly string ContentRoot = Path.Combine(Path.GetTempPath(), "rozwiazanie", "Inżynierka");

    [Fact]
    public void ResolveFile_InsideCache_ReturnsFullPath()
    {
        var path = PhotoCache.ResolveFile(ContentRoot, ".local/photo-cache/abc.jpg");

        Assert.Equal(Path.Combine(PhotoCache.Root(ContentRoot), "abc.jpg"), path);
    }

    [Theory]
    [InlineData(".local/photo-cache/../supabase.json")]
    [InlineData(".local/supabase.json")]
    [InlineData("../../Windows/win.ini")]
    [InlineData(".local/photo-cache")]
    public void ResolveFile_OutsideCache_ReturnsNull(string relative)
    {
        Assert.Null(PhotoCache.ResolveFile(ContentRoot, relative));
    }

    [Theory]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.JPEG", "image/jpeg")]
    [InlineData("a.png", "image/png")]
    [InlineData("a.webp", "image/webp")]
    [InlineData("a.gif", null)]
    public void ContentType_ByExtension(string file, string? expected)
    {
        Assert.Equal(expected, PhotoCache.ContentType(file));
    }
}
