namespace Inżynierka.Models;

public sealed record RegionListResponse(IReadOnlyList<RegionResponse> Items);

// Środek i promień obszaru; RadiusKm można przekazać bezpośrednio do GET /api/attractions.
public sealed record RegionResponse(
    string Code,
    string Name,
    double CenterLatitude,
    double CenterLongitude,
    double RadiusKm,
    bool IsDefault);
