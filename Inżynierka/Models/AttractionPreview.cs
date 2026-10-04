namespace Inżynierka.Models;

public sealed record AttractionPreview(
    string OsmType,
    long OsmId,
    string Name,
    string[] CategoryCodes,
    double Latitude,
    double Longitude,
    bool IsApproximateLocation,
    string? OpeningHours,
    string SourceUrl);

public sealed record AttractionPreviewResponse(
    string City,
    double CenterLatitude,
    double CenterLongitude,
    int RadiusMeters,
    DateTimeOffset FetchedAtUtc,
    int RawElementCount,
    int SkippedElementCount,
    IReadOnlyList<AttractionPreview> Attractions)
{
    public int Count => Attractions.Count;
    public string Attribution => "© OpenStreetMap contributors";
    public string LicenseUrl => "https://www.openstreetmap.org/copyright";
}
