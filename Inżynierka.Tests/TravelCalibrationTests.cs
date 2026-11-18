using Inżynierka.Planning;

namespace Inżynierka.Tests;

public class TravelCalibrationTests
{
    // Dane bez szumu wygenerowane ze znanych parametrów – kalibracja powinna je odtworzyć.
    private static IEnumerable<CalibrationSample> Driving(double factor, double speedKmh, double overheadMinutes) =>
        Enumerable.Range(1, 60).Select(i =>
        {
            var straight = i * 500.0; // 0,5–30 km
            var route = straight * factor;
            return new CalibrationSample(TravelMode.Driving, straight, route, (overheadMinutes + route / (speedKmh * 1000 / 60)) * 60);
        });

    private static IEnumerable<CalibrationSample> Walking(double factor, double speedKmh) =>
        Enumerable.Range(1, 30).Select(i =>
        {
            var straight = 200 + i * 90.0; // 0,3–2,9 km
            var route = straight * factor;
            return new CalibrationSample(TravelMode.Walking, straight, route, route / (speedKmh * 1000 / 60) * 60);
        });

    [Fact]
    public void RecoversKnownParameters()
    {
        var samples = Driving(1.25, 55, 2.5).Concat(Walking(1.2, 5)).ToList();

        var report = TravelCalibration.Calibrate(samples, new TravelEstimateSettings());

        Assert.Equal(1.25, report.Suggested.DrivingDetourFactor, precision: 2);
        Assert.Equal(55, report.Suggested.DrivingSpeedKmh, precision: 1);
        Assert.Equal(2.5, report.Suggested.DrivingOverheadMinutes, precision: 1);
        Assert.Equal(1.2, report.Suggested.WalkingDetourFactor, precision: 2);
        Assert.Equal(5, report.Suggested.WalkingSpeedKmh, precision: 2);
    }

    [Fact]
    public void AfterCalibration_ErrorIsNearZero_AndSmallerThanBefore()
    {
        var report = TravelCalibration.Calibrate(Driving(1.25, 55, 2.5).ToList(), new TravelEstimateSettings());

        var all = Assert.Single(report.Errors, e => e.Mode == TravelMode.Driving && e.DistanceRange == "wszystkie");
        Assert.True(all.After.MedianAbsoluteMinutes <= 0.1);
        Assert.True(all.After.MedianAbsoluteMinutes < all.Before.MedianAbsoluteMinutes);
    }

    [Fact]
    public void ParkingAndWalkingThreshold_AreNotChanged()
    {
        var current = new TravelEstimateSettings { ParkingMinutes = 7, MaxWalkingMinutes = 12 };

        var report = TravelCalibration.Calibrate(Driving(1.3, 50, 1).Concat(Walking(1.2, 5)).ToList(), current);

        Assert.Equal(7, report.Suggested.ParkingMinutes);
        Assert.Equal(12, report.Suggested.MaxWalkingMinutes);
    }

    [Fact]
    public void TooFewSamples_KeepsCurrentValues()
    {
        var current = new TravelEstimateSettings();

        var report = TravelCalibration.Calibrate(Driving(1.25, 55, 2.5).Take(5).ToList(), current);

        Assert.Equal(current.DrivingSpeedKmh, report.Suggested.DrivingSpeedKmh);
        Assert.Equal(current.DrivingDetourFactor, report.Suggested.DrivingDetourFactor);
        Assert.Equal(0, report.WalkingSamples);
    }

    [Fact]
    public void Errors_AreSplitByDistance()
    {
        var report = TravelCalibration.Calibrate(Driving(1.25, 55, 2.5).ToList(), new TravelEstimateSettings());

        var ranges = report.Errors.Where(e => e.Mode == TravelMode.Driving).Select(e => e.DistanceRange).ToList();
        Assert.Equal(new[] { "do 2 km", "2–10 km", "powyżej 10 km", "wszystkie" }, ranges);
        Assert.Equal(60, report.Errors.Single(e => e.DistanceRange == "wszystkie").Samples);
    }

    [Fact]
    public void Percentile_Interpolates()
    {
        Assert.Equal(2.5, TravelCalibration.Percentile([1, 2, 3, 4], 0.5));
        Assert.Equal(3.7, TravelCalibration.Percentile([1, 2, 3, 4], 0.9), precision: 6);
    }

    [Fact]
    public void LinearFit_ExactLine()
    {
        var (intercept, slope) = TravelCalibration.LinearFit([(0, 3), (1, 5), (2, 7)]);

        Assert.Equal(3, intercept, precision: 6);
        Assert.Equal(2, slope, precision: 6);
    }
}
