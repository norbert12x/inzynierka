using Inżynierka.Planning;

namespace Inżynierka.Tests;

// Domyślne parametry szacunku: parkowanie 5 min, pieszo do 15 min.
public class TravelTimeMatrixTests
{
    private static readonly GeoPoint Rynek = new(50.0375, 22.0049);
    private static readonly GeoPoint Zamek = new(50.0333, 22.0018);
    private static readonly GeoPoint Lancut = new(50.0687, 22.2317);

    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static TravelTimeMatrix Matrix(params ((Guid, Guid, TravelMode) Key, KnownTravelTime Time)[] known) =>
        new(new TravelTimeEstimator(new TravelEstimateSettings()), known.ToDictionary(k => k.Key, k => k.Time));

    [Fact]
    public void SamePlace_IsZero()
    {
        var leg = Matrix().GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Rynek, A));

        Assert.Equal(0, leg.Minutes);
        Assert.False(leg.IsApproximate);
    }

    [Fact]
    public void KnownShortWalk_UsesOrsWalking()
    {
        var leg = Matrix(((A, B, TravelMode.Walking), new KnownTravelTime(430, 600)))
            .GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Zamek, B));

        Assert.Equal(new TravelLeg(TravelMode.Walking, 8, 600, TravelTimeSource.OpenRouteService), leg); // 7,2 → 8 min
    }

    [Fact]
    public void KnownLongWalk_FallsBackToOrsDriving_WithParking()
    {
        var leg = Matrix(
                ((A, B, TravelMode.Walking), new KnownTravelTime(1_200, 1_500)), // 20 min – za długo
                ((A, B, TravelMode.Driving), new KnownTravelTime(240, 1_900)))
            .GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Zamek, B));

        Assert.Equal(new TravelLeg(TravelMode.Driving, 4 + 5, 1_900, TravelTimeSource.OpenRouteService), leg);
    }

    [Fact]
    public void OnlyDrivingKnown_UsesOrsDriving()
    {
        var leg = Matrix(((A, B, TravelMode.Driving), new KnownTravelTime(1_620, 20_100)))
            .GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Lancut, B));

        Assert.Equal(TravelMode.Driving, leg.Mode);
        Assert.Equal(27 + 5, leg.Minutes);
        Assert.Equal(TravelTimeSource.OpenRouteService, leg.Source);
    }

    [Fact]
    public void DirectionMatters()
    {
        var leg = Matrix(((A, B, TravelMode.Driving), new KnownTravelTime(1_620, 20_100)))
            .GetLeg(new PlanPoint(Lancut, B), new PlanPoint(Rynek, A));

        Assert.True(leg.IsApproximate);
    }

    [Fact]
    public void Lodging_UsesEstimate()
    {
        var leg = Matrix(((A, B, TravelMode.Driving), new KnownTravelTime(1_620, 20_100)))
            .GetLeg(new PlanPoint(Rynek), new PlanPoint(Lancut, B));

        Assert.Equal(TravelTimeSource.Estimate, leg.Source);
        Assert.Equal(TravelMode.Driving, leg.Mode);
    }

    [Fact]
    public void UnknownPair_UsesEstimate_WithAutomaticMode()
    {
        var leg = Matrix().GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Zamek, B));

        Assert.Equal(TravelTimeSource.Estimate, leg.Source);
        Assert.Equal(TravelMode.Walking, leg.Mode); // ok. 500 m
    }

    [Fact]
    public void UnreachablePair_UsesEstimate()
    {
        var leg = Matrix(((A, B, TravelMode.Driving), new KnownTravelTime(null, null)))
            .GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Lancut, B));

        Assert.True(leg.IsApproximate);
    }

    [Fact]
    public void WalkTooLongAndNoDriving_EstimateIsDriving()
    {
        // Blisko w linii prostej, ale ORS pieszo 25 min (np. rzeka bez mostu) – szacunek nie może wybrać spaceru.
        var leg = Matrix(((A, B, TravelMode.Walking), new KnownTravelTime(1_500, 2_000)))
            .GetLeg(new PlanPoint(Rynek, A), new PlanPoint(Zamek, B));

        Assert.Equal(TravelMode.Driving, leg.Mode);
        Assert.True(leg.IsApproximate);
    }
}
