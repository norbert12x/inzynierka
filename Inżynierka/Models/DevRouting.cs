using System.ComponentModel.DataAnnotations;
using Inżynierka.Planning;

namespace Inżynierka.Models;

// Typy nullowalne + [Required]: brak parametru to błąd 400, a nie punkt (0, 0).
public sealed class TravelEstimateQuery
{
    [Required(ErrorMessage = "Podaj szerokość punktu startu.")]
    [Range(-90, 90, ErrorMessage = "Szerokość od -90 do 90.")]
    public double? FromLatitude { get; set; }

    [Required(ErrorMessage = "Podaj długość punktu startu.")]
    [Range(-180, 180, ErrorMessage = "Długość od -180 do 180.")]
    public double? FromLongitude { get; set; }

    [Required(ErrorMessage = "Podaj szerokość celu.")]
    [Range(-90, 90, ErrorMessage = "Szerokość od -90 do 90.")]
    public double? ToLatitude { get; set; }

    [Required(ErrorMessage = "Podaj długość celu.")]
    [Range(-180, 180, ErrorMessage = "Długość od -180 do 180.")]
    public double? ToLongitude { get; set; }
}

// Chosen – odcinek, który wybrałby planer; Walking i Driving – oba tryby do porównania z Google Maps.
public sealed record TravelEstimateResponse(
    int StraightLineMeters,
    TravelLeg Chosen,
    TravelLeg Walking,
    TravelLeg Driving,
    TravelEstimateSettings Settings);

// --- Macierz atrakcji z OpenRouteService (POST /api/dev/routing/matrix) ---

public sealed record TravelMatrixResponse(
    string Profile,
    bool DryRun,
    // Aktywne atrakcje katalogu.
    int Places,
    // Próg odległości w linii prostej dla tego profilu (Routing:Matrix).
    double MaxDistanceKm,
    // Pary w progu, których jeszcze nie ma w bazie (przed tym wywołaniem).
    int MissingPairs,
    // Zapytania do ORS potrzebne na wszystkie brakujące pary (każde zużywa 1 z 500 dziennie).
    int RequestsNeeded,
    int RequestsSent,
    int PairsSaved,
    // Zapisane pary, dla których ORS nie znalazł trasy (null) – planer użyje szacunku.
    int UnreachablePairs,
    // Zapytania, które zostały na kolejne wywołanie (maxRequests albo dryRun).
    int RemainingRequests,
    string Attribution,
    string License);
