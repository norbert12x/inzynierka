using Inżynierka.Planning;

namespace Inżynierka.Tests;

// Domyślne parametry: auto ×1,35 przy 45 km/h (750 m/min) + 5 min parkowania; pieszo ×1,25 przy 4,5 km/h (75 m/min);
// pieszo, gdy marsz ≤ 15 min.
public class TravelTimeEstimatorTests
{
    private static TravelTimeEstimator Estimator(Action<TravelEstimateSettings>? change = null)
    {
        var settings = new TravelEstimateSettings();
        change?.Invoke(settings);
        return new TravelTimeEstimator(settings);
    }

    [Fact]
    public void SamePoint_IsZeroMinutesOnFoot()
    {
        var point = new GeoPoint(50.0375, 22.0049);

        var leg = Estimator().Estimate(point, point);

        Assert.Equal(new TravelLeg(TravelMode.Walking, 0, 0, TravelTimeSource.Estimate), leg);
    }

    [Fact]
    public void ShortDistance_IsWalking_RoundedUp()
    {
        // 500 m × 1,25 = 625 m / 75 m/min = 8,33 → 9 min.
        var leg = Estimator().EstimateForDistance(500);

        Assert.Equal(TravelMode.Walking, leg.Mode);
        Assert.Equal(9, leg.Minutes);
        Assert.Equal(625, leg.DistanceMeters);
    }

    [Fact]
    public void LongDistance_IsDriving_WithParking()
    {
        // 10 km × 1,35 = 13,5 km / 750 m/min = 18 min (dokładnie – bez zaokrąglenia do 19) + 5 min parkowania.
        var leg = Estimator().EstimateForDistance(10_000);

        Assert.Equal(TravelMode.Driving, leg.Mode);
        Assert.Equal(23, leg.Minutes);
        Assert.Equal(13_500, leg.DistanceMeters);
    }

    [Theory]
    [InlineData(900.0, TravelMode.Walking)]  // marsz dokładnie 15 min – jeszcze pieszo
    [InlineData(901.0, TravelMode.Driving)]  // 15,02 → 16 min – już samochodem
    public void WalkingThreshold_IsInclusive(double meters, TravelMode expected)
    {
        Assert.Equal(expected, Estimator().EstimateForDistance(meters).Mode);
    }

    [Fact]
    public void ThresholdZero_DrivesEvenShortDistances()
    {
        var estimator = Estimator(s => s.MaxWalkingMinutes = 0);

        Assert.Equal(TravelMode.Driving, estimator.EstimateForDistance(50).Mode);
        Assert.Equal(TravelMode.Walking, estimator.EstimateForDistance(0).Mode);
    }

    [Fact]
    public void ForcedDriving_AddsParkingEvenForShortDistance()
    {
        // 500 m × 1,35 = 675 m / 750 m/min = 0,9 → 1 min + 5 min parkowania.
        var leg = Estimator().EstimateForDistance(500, TravelMode.Driving);

        Assert.Equal(TravelMode.Driving, leg.Mode);
        Assert.Equal(6, leg.Minutes);
    }

    [Fact]
    public void Estimate_IsMarkedApproximate()
    {
        var leg = Estimator().EstimateForDistance(3_000);

        Assert.Equal(TravelTimeSource.Estimate, leg.Source);
        Assert.True(leg.IsApproximate);
    }

    [Fact]
    public void SettingsChange_ChangesResult()
    {
        // Wolniejsza jazda (30 km/h = 500 m/min): 13,5 km → 27 min + 5.
        var leg = Estimator(s => s.DrivingSpeedKmh = 30).EstimateForDistance(10_000);

        Assert.Equal(32, leg.Minutes);
    }

    [Fact]
    public void DrivingOverhead_IsAdded_ButNotForZeroDistance()
    {
        var estimator = Estimator(s => s.DrivingOverheadMinutes = 2.5);

        // 18 min jazdy + 2,5 narzutu = 20,5 → 21, + 5 parkowania.
        Assert.Equal(26, estimator.EstimateForDistance(10_000, TravelMode.Driving).Minutes);
        Assert.Equal(0, estimator.MovingMinutes(0, TravelMode.Driving));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDistance_Throws(double meters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Estimator().EstimateForDistance(meters));
    }

    [Fact]
    public void StraightLine_OneDegreeOfLatitude_Is111Km()
    {
        var meters = TravelTimeEstimator.StraightLineMeters(new GeoPoint(50, 22), new GeoPoint(51, 22));

        Assert.InRange(meters, 111_194, 111_196);
    }

    [Fact]
    public void StraightLine_IsSymmetric_AndRealisticInRegion()
    {
        // Przykładowe punkty: rynek w Rzeszowie i okolice zamku w Łańcucie (ok. 16,5 km w linii prostej).
        var rzeszow = new GeoPoint(50.0375, 22.0049);
        var lancut = new GeoPoint(50.0687, 22.2317);

        var there = TravelTimeEstimator.StraightLineMeters(rzeszow, lancut);
        var back = TravelTimeEstimator.StraightLineMeters(lancut, rzeszow);

        Assert.Equal(there, back, precision: 6);
        Assert.InRange(there, 16_000, 17_000);
    }
}
