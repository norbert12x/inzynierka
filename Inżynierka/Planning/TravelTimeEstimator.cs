namespace Inżynierka.Planning;

// Parametry szacunku (sekcja Routing:Estimate w appsettings.json). Wartości startowe – do kalibracji
// na macierzy z OpenRouteService w E3.3.
public sealed class TravelEstimateSettings
{
    // Ile razy trasa jest dłuższa od linii prostej.
    public double DrivingDetourFactor { get; set; } = 1.35;
    // Średnia prędkość jazdy (miasto i drogi lokalne razem).
    public double DrivingSpeedKmh { get; set; } = 45;
    // Stały narzut każdego przejazdu (ruszanie, skrzyżowania, światła) – z kalibracji (E3.3); 0 = brak.
    public double DrivingOverheadMinutes { get; set; }
    // Doliczane do każdego odcinka autem: dojście do auta, parkowanie.
    public int ParkingMinutes { get; set; } = 5;
    public double WalkingDetourFactor { get; set; } = 1.25;
    public double WalkingSpeedKmh { get; set; } = 4.5;
    // Odcinek pieszo, gdy marsz trwa najwyżej tyle minut; dłuższe – samochodem.
    public int MaxWalkingMinutes { get; set; } = 15;
}

// Szacunek czasu przejazdu z odległości w linii prostej (bez bazy i usług zewnętrznych).
// Używany dla odcinków z/do noclegu i par atrakcji spoza macierzy ORS.
public sealed class TravelTimeEstimator(TravelEstimateSettings settings)
{
    // Średni promień Ziemi (IUGG). Wzór haversine – przy odległościach w regionie błąd poniżej 0,5%,
    // czyli dużo mniej niż niepewność samego szacunku.
    private const double EarthRadiusMeters = 6_371_008.8;

    public TravelEstimateSettings Settings => settings;

    // Tryb wybierany automatycznie: pieszo, gdy marsz mieści się w progu, inaczej samochód.
    public TravelLeg Estimate(GeoPoint from, GeoPoint to) => EstimateForDistance(StraightLineMeters(from, to));

    public TravelLeg Estimate(GeoPoint from, GeoPoint to, TravelMode mode)
        => EstimateForDistance(StraightLineMeters(from, to), mode);

    public TravelLeg EstimateForDistance(double straightLineMeters)
    {
        var walking = EstimateForDistance(straightLineMeters, TravelMode.Walking);
        return ShouldWalk(walking.Minutes) ? walking : EstimateForDistance(straightLineMeters, TravelMode.Driving);
    }

    public TravelLeg EstimateForDistance(double straightLineMeters, TravelMode mode)
    {
        var minutes = CeilMinutes(MovingMinutes(straightLineMeters, mode));
        if (mode == TravelMode.Driving)
            minutes += settings.ParkingMinutes;

        return new TravelLeg(mode, minutes, (int)Math.Round(RouteMeters(straightLineMeters, mode)), TravelTimeSource.Estimate);
    }

    // Szacowana długość trasy: linia prosta × współczynnik objazdu.
    public double RouteMeters(double straightLineMeters, TravelMode mode)
    {
        if (!double.IsFinite(straightLineMeters) || straightLineMeters < 0)
            throw new ArgumentOutOfRangeException(nameof(straightLineMeters), "Odległość musi być nieujemną liczbą.");

        return straightLineMeters * (mode == TravelMode.Walking ? settings.WalkingDetourFactor : settings.DrivingDetourFactor);
    }

    // Czas samego ruchu bez zaokrąglenia i bez parkowania – porównywalny z czasem ORS (kalibracja).
    // Jazda: narzut + trasa ÷ prędkość (narzut tylko dla niezerowej odległości).
    public double MovingMinutes(double straightLineMeters, TravelMode mode)
    {
        var routeMeters = RouteMeters(straightLineMeters, mode);
        if (routeMeters == 0)
            return 0;

        return mode == TravelMode.Walking
            ? routeMeters / (settings.WalkingSpeedKmh * 1000 / 60)
            : settings.DrivingOverheadMinutes + routeMeters / (settings.DrivingSpeedKmh * 1000 / 60);
    }

    // Wspólna reguła wyboru trybu – także dla czasów pieszych z macierzy ORS (E3.3).
    public bool ShouldWalk(int walkingMinutes) => walkingMinutes <= settings.MaxWalkingMinutes;

    public static double StraightLineMeters(GeoPoint from, GeoPoint to)
    {
        var lat1 = DegreesToRadians(from.Latitude);
        var lat2 = DegreesToRadians(to.Latitude);
        var dLat = lat2 - lat1;
        var dLon = DegreesToRadians(to.Longitude - from.Longitude);

        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }

    // W górę do pełnej minuty (plan nie powinien być zbyt optymistyczny); zaokrąglenie do 6 miejsc
    // usuwa błędy liczb zmiennoprzecinkowych typu 18,0000000001 → 19.
    public static int CeilMinutes(double minutes) => (int)Math.Ceiling(Math.Round(minutes, 6));

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
