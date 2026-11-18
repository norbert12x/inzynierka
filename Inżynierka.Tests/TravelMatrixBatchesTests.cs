using Inżynierka.Planning;
using Inżynierka.Services;

namespace Inżynierka.Tests;

public class TravelMatrixBatchesTests
{
    private const double FarAway = 1_000_000;

    // Punkty co ok. 100 m na wschód od rynku w Rzeszowie.
    private static List<MatrixPlace> Places(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new MatrixPlace(Guid.NewGuid(), new GeoPoint(50.0375, 22.0049 + i * 0.0014)))
            .ToList();

    private static HashSet<(Guid, Guid)> Covered(IEnumerable<MatrixBatch> batches) =>
        batches.SelectMany(b => b.Pairs.Select(p => (b.Sources[p.Source].Id, b.Destinations[p.Destination].Id))).ToHashSet();

    [Fact]
    public void NoPlaces_NoBatches()
    {
        Assert.Empty(TravelMatrixBatches.Plan([], (_, _) => false, FarAway));
    }

    [Fact]
    public void ThreePlaces_OneBatch_AllDirectedPairsWithoutSelf()
    {
        var places = Places(3);

        var batch = Assert.Single(TravelMatrixBatches.Plan(places, (_, _) => false, FarAway));

        Assert.Equal(6, batch.Pairs.Count);
        Assert.DoesNotContain(Covered([batch]), p => p.Item1 == p.Item2);
    }

    [Fact]
    public void KnownPairs_AreSkipped()
    {
        var places = Places(3);
        var known = new HashSet<(Guid, Guid)> { (places[0].Id, places[1].Id), (places[1].Id, places[0].Id) };

        var covered = Covered(TravelMatrixBatches.Plan(places, (a, b) => known.Contains((a, b)), FarAway));

        Assert.Equal(4, covered.Count);
        Assert.Empty(covered.Intersect(known));
    }

    [Fact]
    public void AllKnown_NoBatches()
    {
        Assert.Empty(TravelMatrixBatches.Plan(Places(4), (_, _) => true, FarAway));
    }

    [Fact]
    public void PairsBeyondDistance_AreSkipped()
    {
        var near = Places(2);
        var far = new MatrixPlace(Guid.NewGuid(), new GeoPoint(49.30, 22.50)); // ok. 85 km na południe

        var covered = Covered(TravelMatrixBatches.Plan([.. near, far], (_, _) => false, maxDistanceMeters: 3_000));

        Assert.Equal(2, covered.Count);
        Assert.DoesNotContain(covered, p => p.Item1 == far.Id || p.Item2 == far.Id);
    }

    [Fact]
    public void ElementLimit_IsRespected_AndEveryPairCoveredOnce()
    {
        var places = Places(10);

        var batches = TravelMatrixBatches.Plan(places, (_, _) => false, FarAway, maxElementsPerRequest: 9);

        Assert.All(batches, b => Assert.True(b.Sources.Count * b.Destinations.Count <= 9));
        var all = batches.SelectMany(b => b.Pairs.Select(p => (b.Sources[p.Source].Id, b.Destinations[p.Destination].Id))).ToList();
        Assert.Equal(10 * 9, all.Count);
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void BatchIndexes_PointInsideSourcesAndDestinations()
    {
        var batches = TravelMatrixBatches.Plan(Places(7), (_, _) => false, FarAway, maxElementsPerRequest: 16);

        Assert.All(batches, b => Assert.All(b.Pairs, p =>
        {
            Assert.InRange(p.Source, 0, b.Sources.Count - 1);
            Assert.InRange(p.Destination, 0, b.Destinations.Count - 1);
        }));
    }
}
