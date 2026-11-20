using System.Globalization;

namespace Inżynierka.Planning;

// Linki nawigacji Google Maps (Maps URLs, bez klucza API i bez wywołań z serwera – link otwiera użytkownik).
// https://developers.google.com/maps/documentation/urls/get-started#directions-action
public static class GoogleMapsLinks
{
    // Maps URLs przyjmują do 9 punktów pośrednich (w przeglądarce mobilnej mniej) – dłuższy dzień dzielimy na kilka linków.
    public const int MaxWaypoints = 9;
    private const string BaseUrl = "https://www.google.com/maps/dir/?api=1";

    // Odcinek w trybie planera (pieszo / samochód).
    public static string Leg(GeoPoint from, GeoPoint to, TravelMode mode)
        => Directions(from, to, mode == TravelMode.Walking ? "walking" : "driving", []);

    // Ten sam odcinek komunikacją publiczną – planer nie liczy rozkładów, Google pokazuje połączenia.
    public static string Transit(GeoPoint from, GeoPoint to) => Directions(from, to, "transit", []);

    // Cały dzień samochodem: start → wizyty → koniec; każdy kolejny link zaczyna się tam, gdzie skończył poprzedni.
    public static IReadOnlyList<string> Day(GeoPoint start, IReadOnlyList<GeoPoint> stops, GeoPoint end)
    {
        var points = new List<GeoPoint>(stops.Count + 2) { start };
        points.AddRange(stops);
        points.Add(end);

        var links = new List<string>();
        var first = 0;
        do
        {
            var last = Math.Min(first + MaxWaypoints + 1, points.Count - 1);
            links.Add(Directions(points[first], points[last], "driving", points.GetRange(first + 1, last - first - 1)));
            first = last;
        }
        while (first < points.Count - 1);
        return links;
    }

    private static string Directions(GeoPoint from, GeoPoint to, string travelMode, IReadOnlyList<GeoPoint> waypoints)
    {
        var url = $"{BaseUrl}&origin={Point(from)}&destination={Point(to)}&travelmode={travelMode}";
        if (waypoints.Count > 0)
            url += "&waypoints=" + string.Join("%7C", waypoints.Select(Point));
        return url;
    }

    private static string Point(GeoPoint point) => Uri.EscapeDataString(
        $"{point.Latitude.ToString("0.######", CultureInfo.InvariantCulture)},{point.Longitude.ToString("0.######", CultureInfo.InvariantCulture)}");
}
