using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Inżynierka.Services;

// Miejsce z katalogu do porównania (wszystkie statusy – także usunięte, żeby nie dodać ich ponownie po cichu).
// SourceKeys: identyfikatory w źródłach, np. "node/123" (OSM) albo "overture:<id>".
public sealed record ExistingPlace(Guid Id, string Name, string Status, double Latitude, double Longitude,
    IReadOnlyCollection<string> SourceKeys);

// Level: same_source (ten sam obiekt w źródle – OSM albo Overture – blokada), likely (blisko i podobna nazwa – wymaga potwierdzenia),
// nearby (coś w tym samym miejscu – tylko informacja, np. kilka atrakcji w jednej galerii handlowej).
public sealed record DuplicateMatch(Guid PlaceId, string Name, string Status, int DistanceMeters, string Level, string Reason);

public static class PlaceDuplicates
{
    public const string SameSource = "same_source";
    public const string Likely = "likely";
    public const string Nearby = "nearby";

    public const double LikelyRadiusMeters = 150;
    public const double SameNameRadiusMeters = 1000;
    public const double NearbyRadiusMeters = 25;

    // Klucz źródła dla miejsc z Overture: "overture:" + identyfikator Overture (GERS).
    public const string OverturePrefix = "overture:";

    public static string OvertureKey(string id) => OverturePrefix + id;

    // Słowa zbyt ogólne, żeby świadczyły o tym samym miejscu.
    private static readonly HashSet<string> GenericWords =
    [
        "escape", "room", "rooms", "rzeszow", "rzeszowie", "park", "centrum", "center", "centre", "club", "klub",
        "sp", "z", "o", "i", "w", "na", "the", "and", "pl", "s", "a", "bowling", "kregielnia", "laser", "tag",
        "lasertag", "arena", "studio", "strefa", "sala", "zabaw", "trampolin", "trampoline", "gokart", "gokarty",
        "karting", "paintball", "vr", "wspinaczkowe", "wspinaczkowa", "scianka", "strzelnica"
    ];

    // Wszystkie możliwe duplikaty, od najpoważniejszych; pusta lista = można dodać bez pytania.
    public static IReadOnlyList<DuplicateMatch> Find(string name, double latitude, double longitude, string? sourceKey,
        IEnumerable<ExistingPlace> existing)
    {
        var matches = new List<DuplicateMatch>();
        foreach (var place in existing)
        {
            var distance = DistanceMeters(latitude, longitude, place.Latitude, place.Longitude);
            var rounded = (int)Math.Round(distance);
            if (sourceKey is not null && place.SourceKeys.Contains(sourceKey))
            {
                var source = sourceKey.StartsWith(OverturePrefix, StringComparison.Ordinal) ? "Overture" : "OpenStreetMap";
                matches.Add(new DuplicateMatch(place.Id, place.Name, place.Status, rounded, SameSource,
                    $"Ten sam obiekt {source} ({sourceKey})."));
                continue;
            }

            var similarity = NameSimilarity(name, place.Name);
            if (distance <= LikelyRadiusMeters && similarity >= 0.5)
                matches.Add(new DuplicateMatch(place.Id, place.Name, place.Status, rounded, Likely,
                    $"Podobna nazwa w odległości {rounded} m."));
            else if (distance <= SameNameRadiusMeters && similarity >= 1)
                matches.Add(new DuplicateMatch(place.Id, place.Name, place.Status, rounded, Likely,
                    $"Ta sama nazwa w odległości {rounded} m."));
            else if (distance <= NearbyRadiusMeters)
                matches.Add(new DuplicateMatch(place.Id, place.Name, place.Status, rounded, Nearby,
                    $"Inne miejsce w odległości {rounded} m."));
        }

        return matches
            .OrderBy(m => m.Level switch { SameSource => 0, Likely => 1, _ => 2 })
            .ThenBy(m => m.DistanceMeters)
            .ToList();
    }

    // Podobieństwo nazw 0–1 (wspólne słowa / wszystkie słowa) po ujednoliceniu; 1 = te same słowa znaczące.
    public static double NameSimilarity(string first, string second)
    {
        var a = SignificantTokens(first);
        var b = SignificantTokens(second);
        if (a.Count == 0 || b.Count == 0)
            return 0;
        var common = a.Intersect(b).Count();
        var all = a.Union(b).Count();
        // Krótsza nazwa w całości zawarta w dłuższej („Grawitacja” vs „Grawitacja Park Trampolin”) = mocne podobieństwo.
        return a.IsSubsetOf(b) || b.IsSubsetOf(a) ? Math.Max(0.75, (double)common / all) : (double)common / all;
    }

    // Słowa znaczące: małe litery, bez polskich znaków i interpunkcji, bez słów ogólnych.
    // Gdy po odrzuceniu ogólnych nic nie zostaje (np. „Laser Tag Arena”), bierzemy wszystkie słowa.
    public static HashSet<string> SignificantTokens(string name)
    {
        var tokens = Tokenize(name);
        var significant = tokens.Where(t => !GenericWords.Contains(t)).ToHashSet();
        return significant.Count > 0 ? significant : tokens.ToHashSet();
    }

    private static List<string> Tokenize(string name)
    {
        var decomposed = name.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // Odległość po kuli ziemskiej (haversine) w metrach – wystarczająca do porównań w skali kilkuset metrów.
    public static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadius = 6_371_000;
        static double Rad(double degrees) => degrees * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadius * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    // Klucz obiektu OSM, np. "node/123". Typ: n/node, w/way, r/relation.
    public static string? OsmKey(string? type, long id)
    {
        var full = type switch
        {
            "n" or "node" => "node",
            "w" or "way" => "way",
            "r" or "relation" => "relation",
            _ => null
        };
        return full is null || id <= 0 ? null : $"{full}/{id}";
    }

    // Klucz OSM z place_sources.raw_data (sourceDetails.raw.osm_type + osm_id) – działa dla importu Geoapify i OSM.
    public static string? OsmKeyFromRawData(string rawDataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawDataJson);
            if (!document.RootElement.TryGetProperty("sourceDetails", out var details)
                || !details.TryGetProperty("raw", out var raw)
                || !raw.TryGetProperty("osm_type", out var type) || type.ValueKind != JsonValueKind.String
                || !raw.TryGetProperty("osm_id", out var id))
                return null;
            var number = id.ValueKind switch
            {
                JsonValueKind.Number when id.TryGetInt64(out var n) => n,
                JsonValueKind.String when long.TryParse(id.GetString(), out var n) => n,
                _ => 0
            };
            return OsmKey(type.GetString(), number);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
