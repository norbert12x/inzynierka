using System.Text.Json;

namespace Inżynierka.Services;

public sealed record OvertureSource(string Dataset, string? License, string? RecordId);

// Jedna pozycja z data/overture/<wydanie>/candidates.json (scripts/fetch_overture_places.py).
public sealed record OvertureCandidate(
    string Id, string Name, string? Category, IReadOnlyList<string> Hierarchy, string SuggestedCategory,
    double Confidence, string? OperatingStatus, double Latitude, double Longitude, string? Address, string? City,
    string? Postcode, string? Website, string? Phone, string Region, IReadOnlyList<OvertureSource> Sources,
    string Attribution);

public sealed record OvertureCandidatesFile(string Release, DateTimeOffset FetchedAtUtc, string LicenseInfo,
    IReadOnlyList<OvertureCandidate> Items);

// Odczyt najnowszego wyciągu Overture z dysku. Backend nie łączy się z S3 – pobiera skrypt.
public sealed class OvertureCandidatesStore(IWebHostEnvironment environment)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Directory => Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "data", "overture"));

    // null = brak wyciągu (trzeba uruchomić skrypt).
    public async Task<OvertureCandidatesFile?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(Directory))
            return null;
        // Wydania mają nazwy RRRR-MM-DD.N, więc sortowanie tekstowe daje najnowsze na końcu.
        var file = System.IO.Directory.GetDirectories(Directory)
            .Select(d => Path.Combine(d, "candidates.json"))
            .Where(File.Exists)
            .OrderBy(f => f, StringComparer.Ordinal)
            .LastOrDefault();
        if (file is null)
            return null;
        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<OvertureCandidatesFile>(stream, Json, cancellationToken);
    }
}
