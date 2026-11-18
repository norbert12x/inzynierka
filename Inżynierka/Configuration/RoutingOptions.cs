using Inżynierka.Planning;

namespace Inżynierka.Configuration;

// Sekcja "Routing" w appsettings.json: parametry czasów przejazdów (E3). Bez sekretów –
// klucz OpenRouteService jest w .local/ors-key.txt (OpenRouteService:ApiKey).
public sealed class RoutingOptions
{
    public const string SectionName = "Routing";

    public TravelEstimateSettings Estimate { get; set; } = new();
    public TravelMatrixSettings Matrix { get; set; } = new();
}

// Które pary atrakcji pobieramy z ORS (odległość w linii prostej). Dalsze pary – szacunek.
public sealed class TravelMatrixSettings
{
    // MVP (Rzeszów, promień 30 km): tyle, żeby objąć wszystkie pary. Całe Podkarpacie: ok. 20–25 km.
    public double DrivingMaxDistanceKm { get; set; } = 80;
    // Pieszo tylko krótkie odcinki – dłuższe i tak będą samochodem.
    public double WalkingMaxDistanceKm { get; set; } = 3;
}
