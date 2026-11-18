namespace Inżynierka.Data.Entities;

// Czas i długość trasy po drogach między dwiema atrakcjami (routing.travel_times), pobrane z macierzy
// OpenRouteService narzędziem POST /api/dev/routing/matrix. Kierunek ma znaczenie (A→B może różnić się od B→A).
// Wyniki ORS są na licencji CC-BY-SA 4.0 – przy wyświetlaniu podpis OpenRouteServiceMatrixService.Attribution.
public sealed class TravelTime
{
    public Guid FromPlaceId { get; set; }
    public Guid ToPlaceId { get; set; }
    public string Profile { get; set; } = RoutingProfiles.DrivingCar;
    // null = dostawca nie znalazł trasy (np. punkt daleko od drogi) – nie pytamy ponownie, planer użyje szacunku.
    public int? DurationSeconds { get; set; }
    public int? DistanceMeters { get; set; }
    public string Provider { get; set; } = TravelTimeProviders.OpenRouteService;
    public DateTimeOffset FetchedAt { get; set; }
}

// Nazwy profili jak w API OpenRouteService.
public static class RoutingProfiles
{
    public const string DrivingCar = "driving-car";
    public const string FootWalking = "foot-walking";

    public static readonly IReadOnlyList<string> All = [DrivingCar, FootWalking];
}

public static class TravelTimeProviders
{
    public const string OpenRouteService = "openrouteservice";
}
