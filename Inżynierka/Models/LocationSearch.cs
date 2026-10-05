using System.ComponentModel.DataAnnotations;

namespace Inżynierka.Models;

public sealed class LocationSearchQuery
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Text { get; set; } = "";

    [Required, RegularExpression("^(address|city)$")]
    public string Type { get; set; } = "address";
}

public sealed record LocationSearchResponse(
    IReadOnlyList<LocationCandidate> Items,
    string Provider = "Geoapify",
    string Attribution = "Powered by Geoapify | © OpenStreetMap contributors");

public sealed record LocationCandidate(
    string? PlaceId,
    string FormattedAddress,
    string? City,
    string? ResultType,
    double Latitude,
    double Longitude,
    double? Confidence);
