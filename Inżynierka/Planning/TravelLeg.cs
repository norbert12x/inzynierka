using System.Text.Json.Serialization;

namespace Inżynierka.Planning;

// Punkt WGS84 (szerokość, długość) – nocleg albo atrakcja.
public readonly record struct GeoPoint(double Latitude, double Longitude);

// W JSON jako "walking" / "driving".
[JsonConverter(typeof(JsonStringEnumConverter<TravelMode>))]
public enum TravelMode
{
    [JsonStringEnumMemberName("walking")]
    Walking,
    [JsonStringEnumMemberName("driving")]
    Driving
}

// Skąd pochodzi czas odcinka. Szacunek jest w planie oznaczany jako „czas przybliżony”.
[JsonConverter(typeof(JsonStringEnumConverter<TravelTimeSource>))]
public enum TravelTimeSource
{
    // Z odległości w linii prostej (TravelTimeEstimator).
    [JsonStringEnumMemberName("estimate")]
    Estimate,
    // Z macierzy czasów po drogach pobranej z OpenRouteService (E3.2).
    [JsonStringEnumMemberName("openrouteservice")]
    OpenRouteService
}

// Odcinek między dwoma punktami planu. Minutes – z parkowaniem przy jeździe autem;
// DistanceMeters – długość trasy (przy szacunku: linia prosta × współczynnik objazdu).
public sealed record TravelLeg(TravelMode Mode, int Minutes, int DistanceMeters, TravelTimeSource Source)
{
    public bool IsApproximate => Source == TravelTimeSource.Estimate;
}
