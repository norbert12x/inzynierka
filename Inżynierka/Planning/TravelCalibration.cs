namespace Inżynierka.Planning;

// Jedna para z macierzy ORS: odległość w linii prostej oraz długość i czas trasy (bez parkowania).
public sealed record CalibrationSample(TravelMode Mode, double StraightLineMeters, double RouteMeters, double RouteSeconds);

// Błąd szacunku względem ORS (szacunek − ORS, w minutach, bez parkowania i bez zaokrąglania).
public sealed record CalibrationErrorStats(
    double MedianAbsoluteMinutes,
    double P90AbsoluteMinutes,
    double MedianAbsolutePercent,
    // Średni błąd ze znakiem: > 0 – szacunek zawyża, < 0 – zaniża.
    double MeanSignedMinutes);

public sealed record CalibrationErrorRow(TravelMode Mode, string DistanceRange, int Samples,
    CalibrationErrorStats Before, CalibrationErrorStats After);

public sealed record CalibrationReport(
    int DrivingSamples,
    int WalkingSamples,
    TravelEstimateSettings Current,
    // Parametry dopasowane do danych; parkowanie i próg spaceru bez zmian (ORS ich nie zawiera).
    TravelEstimateSettings Suggested,
    IReadOnlyList<CalibrationErrorRow> Errors);

// Kalibracja szacunku na parach z ORS (bez bazy i HTTP – testy xUnit):
// współczynnik objazdu = mediana (trasa ORS ÷ linia prosta); jazda: regresja liniowa czasu względem
// szacowanej trasy → narzut (wyraz wolny) i prędkość (nachylenie); pieszo: regresja przez zero → prędkość.
public static class TravelCalibration
{
    // Za mało par – parametry tego trybu zostają bez zmian.
    public const int MinSamples = 20;
    // Pary bardzo bliskie (wejścia do jednego obiektu) zaburzają iloraz trasa ÷ linia prosta.
    private const double MinStraightLineMeters = 200;

    private static readonly (string Label, double FromMeters, double ToMeters)[] DrivingRanges =
        [("do 2 km", 0, 2_000), ("2–10 km", 2_000, 10_000), ("powyżej 10 km", 10_000, double.PositiveInfinity)];
    private static readonly (string Label, double FromMeters, double ToMeters)[] WalkingRanges =
        [("do 1 km", 0, 1_000), ("1–3 km", 1_000, double.PositiveInfinity)];

    public static CalibrationReport Calibrate(IReadOnlyList<CalibrationSample> samples, TravelEstimateSettings current)
    {
        var driving = samples.Where(s => s.Mode == TravelMode.Driving && IsUsable(s)).ToList();
        var walking = samples.Where(s => s.Mode == TravelMode.Walking && IsUsable(s)).ToList();

        var suggested = Copy(current);
        if (driving.Count >= MinSamples)
        {
            var factor = Median(driving.Select(s => s.RouteMeters / s.StraightLineMeters));
            var (intercept, slope) = LinearFit(driving.Select(s => (s.StraightLineMeters * factor / 1000, s.RouteSeconds / 60)));
            if (slope > 0)
            {
                suggested.DrivingDetourFactor = Math.Round(factor, 2);
                suggested.DrivingSpeedKmh = Math.Round(60 / slope, 1);
                suggested.DrivingOverheadMinutes = Math.Round(Math.Max(0, intercept), 1);
            }
        }
        if (walking.Count >= MinSamples)
        {
            var factor = Median(walking.Select(s => s.RouteMeters / s.StraightLineMeters));
            var slope = FitThroughOrigin(walking.Select(s => (s.StraightLineMeters * factor / 1000, s.RouteSeconds / 60)));
            if (slope > 0)
            {
                suggested.WalkingDetourFactor = Math.Round(factor, 2);
                suggested.WalkingSpeedKmh = Math.Round(60 / slope, 2);
            }
        }

        var before = new TravelTimeEstimator(current);
        var after = new TravelTimeEstimator(suggested);
        var errors = new List<CalibrationErrorRow>();
        foreach (var (mode, list, ranges) in new[] { (TravelMode.Driving, driving, DrivingRanges), (TravelMode.Walking, walking, WalkingRanges) })
        {
            foreach (var (label, from, to) in ranges)
            {
                var bucket = list.Where(s => s.StraightLineMeters >= from && s.StraightLineMeters < to).ToList();
                if (bucket.Count > 0)
                    errors.Add(new CalibrationErrorRow(mode, label, bucket.Count, Stats(bucket, before), Stats(bucket, after)));
            }
            if (list.Count > 0)
                errors.Add(new CalibrationErrorRow(mode, "wszystkie", list.Count, Stats(list, before), Stats(list, after)));
        }

        return new CalibrationReport(driving.Count, walking.Count, Copy(current), suggested, errors);
    }

    private static bool IsUsable(CalibrationSample s) =>
        s.StraightLineMeters >= MinStraightLineMeters && s.RouteMeters > 0 && s.RouteSeconds > 0
        && double.IsFinite(s.RouteMeters) && double.IsFinite(s.RouteSeconds);

    private static CalibrationErrorStats Stats(IReadOnlyList<CalibrationSample> samples, TravelTimeEstimator estimator)
    {
        var errors = samples.Select(s =>
        {
            var actual = s.RouteSeconds / 60;
            var error = estimator.MovingMinutes(s.StraightLineMeters, s.Mode) - actual;
            return (Error: error, Percent: Math.Abs(error) / actual * 100);
        }).ToList();
        var absolute = errors.Select(e => Math.Abs(e.Error)).ToList();

        return new CalibrationErrorStats(
            Math.Round(Median(absolute), 1),
            Math.Round(Percentile(absolute, 0.9), 1),
            Math.Round(Median(errors.Select(e => e.Percent)), 1),
            Math.Round(errors.Average(e => e.Error), 1));
    }

    // Najmniejsze kwadraty: y = intercept + slope · x.
    public static (double Intercept, double Slope) LinearFit(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        var meanX = list.Average(p => p.X);
        var meanY = list.Average(p => p.Y);
        var covariance = list.Sum(p => (p.X - meanX) * (p.Y - meanY));
        var variance = list.Sum(p => (p.X - meanX) * (p.X - meanX));
        if (variance == 0)
            return (meanY, 0);
        var slope = covariance / variance;
        return (meanY - slope * meanX, slope);
    }

    // Najmniejsze kwadraty bez wyrazu wolnego: y = slope · x.
    public static double FitThroughOrigin(IEnumerable<(double X, double Y)> points)
    {
        var list = points.ToList();
        var denominator = list.Sum(p => p.X * p.X);
        return denominator == 0 ? 0 : list.Sum(p => p.X * p.Y) / denominator;
    }

    public static double Median(IEnumerable<double> values) => Percentile(values.ToList(), 0.5);

    // Percentyl z interpolacją liniową między sąsiednimi wartościami.
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
            return double.NaN;
        var sorted = values.OrderBy(v => v).ToList();
        var position = (sorted.Count - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static TravelEstimateSettings Copy(TravelEstimateSettings s) => new()
    {
        DrivingDetourFactor = s.DrivingDetourFactor,
        DrivingSpeedKmh = s.DrivingSpeedKmh,
        DrivingOverheadMinutes = s.DrivingOverheadMinutes,
        ParkingMinutes = s.ParkingMinutes,
        WalkingDetourFactor = s.WalkingDetourFactor,
        WalkingSpeedKmh = s.WalkingSpeedKmh,
        MaxWalkingMinutes = s.MaxWalkingMinutes
    };
}
