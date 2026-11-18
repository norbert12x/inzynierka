using Inżynierka.Data;
using Inżynierka.Data.Entities;
using Inżynierka.Planning;
using Microsoft.EntityFrameworkCore;

namespace Inżynierka.Services;

// Ładuje z routing.travel_times czasy między atrakcjami planu i buduje TravelTimeMatrix dla planera (E4).
// Nie wywołuje ORS – brakujące pary i odcinki z noclegu liczy szacunek.
public sealed class TravelTimeService(CatalogDbContext database, TravelTimeEstimator estimator)
{
    public async Task<TravelTimeMatrix> ForPlacesAsync(IEnumerable<Guid> placeIds, CancellationToken cancellationToken)
    {
        var ids = placeIds.Distinct().ToList();
        var known = new Dictionary<(Guid From, Guid To, TravelMode Mode), KnownTravelTime>();
        if (ids.Count < 2)
            return new TravelTimeMatrix(estimator, known);

        var rows = await database.TravelTimes.AsNoTracking()
            .Where(t => ids.Contains(t.FromPlaceId) && ids.Contains(t.ToPlaceId))
            .Select(t => new { t.FromPlaceId, t.ToPlaceId, t.Profile, t.DurationSeconds, t.DistanceMeters })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            TravelMode? mode = row.Profile switch
            {
                RoutingProfiles.DrivingCar => TravelMode.Driving,
                RoutingProfiles.FootWalking => TravelMode.Walking,
                _ => null
            };
            if (mode is not null)
                known[(row.FromPlaceId, row.ToPlaceId, mode.Value)] = new KnownTravelTime(row.DurationSeconds, row.DistanceMeters);
        }

        return new TravelTimeMatrix(estimator, known);
    }
}
