using System.Text.Json;
using Inżynierka.Models;

namespace Inżynierka.Services;

public sealed class GeoapifyGeocodingService(HttpClient client, IConfiguration configuration)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Geoapify:ApiKey"]);

    public async Task<LocationSearchResponse> SearchAsync(string text, string type, CancellationToken cancellationToken)
    {
        var path = "v1/geocode/search?format=json&lang=pl&limit=5&filter=countrycode:pl"
            + $"&text={Uri.EscapeDataString(text)}&apiKey={Uri.EscapeDataString(configuration["Geoapify:ApiKey"]!)}";
        if (type == "city") path += "&type=city";

        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new JsonException("Brak listy wyników geokodowania.");

        var items = new List<LocationCandidate>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("lat", out var lat) || lat.ValueKind != JsonValueKind.Number || !lat.TryGetDouble(out var latitude)
                || !item.TryGetProperty("lon", out var lon) || lon.ValueKind != JsonValueKind.Number || !lon.TryGetDouble(out var longitude)
                || !double.IsFinite(latitude) || !double.IsFinite(longitude)
                || latitude is < -90 or > 90 || longitude is < -180 or > 180)
                throw new JsonException("Nieprawidłowe współrzędne geokodowania.");
            var formatted = GetString(item, "formatted");
            if (string.IsNullOrWhiteSpace(formatted)) throw new JsonException("Brak adresu wyniku.");

            double? confidence = null;
            if (item.TryGetProperty("rank", out var rank) && rank.ValueKind == JsonValueKind.Object
                && rank.TryGetProperty("confidence", out var score) && score.ValueKind == JsonValueKind.Number
                && score.TryGetDouble(out var value) && double.IsFinite(value))
                confidence = value;

            items.Add(new LocationCandidate(GetString(item, "place_id"), formatted,
                GetString(item, "city") ?? GetString(item, "town") ?? GetString(item, "village"),
                GetString(item, "result_type"), latitude, longitude, confidence));
        }

        return new LocationSearchResponse(items);
    }

    private static string? GetString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
