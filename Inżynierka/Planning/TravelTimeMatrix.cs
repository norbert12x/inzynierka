namespace Inżynierka.Planning;

// Punkt planu: atrakcja z katalogu (PlaceId) albo nocleg / dowolny adres (PlaceId = null).
public sealed record PlanPoint(GeoPoint Location, Guid? PlaceId = null);

// Czas po drogach z bazy (routing.travel_times); DurationSeconds = null – dostawca nie znalazł trasy.
public sealed record KnownTravelTime(int? DurationSeconds, int? DistanceMeters);

// Czasy odcinków dla planera: para atrakcji z macierzy ORS, inaczej szacunek (oznaczony jako przybliżony).
// Bez bazy – TravelTimeService ładuje znane pary, testy podają je ręcznie.
public sealed class TravelTimeMatrix(
    TravelTimeEstimator estimator,
    IReadOnlyDictionary<(Guid From, Guid To, TravelMode Mode), KnownTravelTime> known)
{
    public TravelLeg GetLeg(PlanPoint from, PlanPoint to)
    {
        if (from.PlaceId is { } fromId && to.PlaceId is { } toId)
        {
            if (fromId == toId)
                return new TravelLeg(TravelMode.Walking, 0, 0, TravelTimeSource.OpenRouteService);

            // Pieszo, jeśli ORS zna trasę pieszą i marsz mieści się w progu (ta sama reguła co w szacunku).
            var walkTooLong = false;
            if (Known(fromId, toId, TravelMode.Walking) is { } walk)
            {
                var minutes = TravelTimeEstimator.CeilMinutes(walk.Seconds / 60.0);
                if (estimator.ShouldWalk(minutes))
                    return new TravelLeg(TravelMode.Walking, minutes, walk.Meters, TravelTimeSource.OpenRouteService);
                walkTooLong = true;
            }

            if (Known(fromId, toId, TravelMode.Driving) is { } drive)
                return new TravelLeg(TravelMode.Driving,
                    TravelTimeEstimator.CeilMinutes(drive.Seconds / 60.0) + estimator.Settings.ParkingMinutes,
                    drive.Meters, TravelTimeSource.OpenRouteService);

            // ORS wie, że pieszo za daleko – szacunek nie może wybrać spaceru.
            if (walkTooLong)
                return estimator.Estimate(from.Location, to.Location, TravelMode.Driving);
        }

        // Nocleg, nowa atrakcja bez pobranej macierzy albo para bez trasy.
        return estimator.Estimate(from.Location, to.Location);
    }

    private (int Seconds, int Meters)? Known(Guid from, Guid to, TravelMode mode)
        => known.TryGetValue((from, to, mode), out var time) && time.DurationSeconds is { } seconds
            ? (seconds, time.DistanceMeters ?? 0)
            : null;
}
