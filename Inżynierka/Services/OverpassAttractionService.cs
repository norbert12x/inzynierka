using Inżynierka.Models;

namespace Inżynierka.Services;

public sealed class OverpassAttractionService(HttpClient httpClient)
{
    // Stały obszar pierwszej próby. Nie jest to pełny katalog miasta.
    private const double Latitude = 50.037;
    private const double Longitude = 22.004;
    private const int RadiusMeters = 3000;

    public async Task<AttractionPreviewResponse> GetRzeszowAsync(CancellationToken cancellationToken)
    {
        // nwr obejmuje punkty, obszary i relacje. Dla obszarów prosimy o środek.
        var query = FormattableString.Invariant($"""
            [out:json][timeout:45];
            (
              nwr(around:{RadiusMeters},{Latitude},{Longitude})["tourism"~"^(attraction|museum|gallery|viewpoint|zoo|theme_park)$"];
              nwr(around:{RadiusMeters},{Latitude},{Longitude})["historic"~"^(castle|ruins|monument|memorial|archaeological_site|manor)$"];
            );
            out body center;
            """);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
        using var response = await httpClient.PostAsync("interpreter", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<OverpassResponse>(cancellationToken);
        // Overpass może zwrócić HTTP 200 z błędem i niepełnym zestawem elementów.
        if (data?.Elements is null || !string.IsNullOrWhiteSpace(data.Remark))
            throw new InvalidDataException("Overpass zwrócił niepełną lub niepoprawną odpowiedź.");

        var attractions = new List<AttractionPreview>();
        var identifiers = new HashSet<(string, long)>();

        foreach (var element in data.Elements)
        {
            var name = GetTag(element.Tags, "name:pl") ?? GetTag(element.Tags, "name");
            var latitude = element.Type == "node" ? element.Lat : element.Center?.Lat;
            var longitude = element.Type == "node" ? element.Lon : element.Center?.Lon;

            if (name is null || element.Type is not ("node" or "way" or "relation") || element.Id <= 0
                || latitude is null || longitude is null
                || !double.IsFinite(latitude.Value) || !double.IsFinite(longitude.Value)
                || latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180)
                continue;

            if (!identifiers.Add((element.Type, element.Id)))
                continue;

            attractions.Add(new AttractionPreview(
                element.Type, element.Id, name, MapCategories(element.Tags),
                latitude.Value, longitude.Value, element.Type != "node",
                GetTag(element.Tags, "opening_hours"),
                $"https://www.openstreetmap.org/{element.Type}/{element.Id}"));
        }

        return new AttractionPreviewResponse(
            "Rzeszów", Latitude, Longitude, RadiusMeters, DateTimeOffset.UtcNow,
            data.Elements.Count, data.Elements.Count - attractions.Count,
            attractions.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? GetTag(Dictionary<string, string>? tags, string key)
        => tags is not null && tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim() : null;

    private static string[] MapCategories(Dictionary<string, string>? tags)
    {
        var categories = new List<string>();
        switch (GetTag(tags, "tourism"))
        {
            case "museum":
            case "gallery": categories.Add("museums"); break;
            case "viewpoint": categories.Add("viewpoints"); break;
            case "zoo":
            case "theme_park": categories.Add("recreation"); break;
        }

        if (GetTag(tags, "historic") is "castle" or "ruins" or "monument" or "memorial" or "archaeological_site" or "manor")
            categories.Add("heritage");
        if (GetTag(tags, "amenity") == "place_of_worship")
            categories.Add("religious-sites");

        // Samo tourism=attraction nie mówi, do której kategorii należy miejsce.
        return categories.ToArray();
    }

    // Modele odpowiedzi dostawcy są oddzielone od odpowiedzi naszego API.
    private sealed class OverpassResponse
    {
        public OverpassResponse() { }
        public List<OverpassElement>? Elements { get; set; }
        public string? Remark { get; set; }
    }

    private sealed class OverpassElement
    {
        public OverpassElement() { }
        public string? Type { get; set; }
        public long Id { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
        public OverpassCenter? Center { get; set; }
        public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class OverpassCenter
    {
        public OverpassCenter() { }
        public double? Lat { get; set; }
        public double? Lon { get; set; }
    }
}
