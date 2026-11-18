using Microsoft.Extensions.Options;

namespace Inżynierka.Configuration;

// Sprawdzane przy starcie (ValidateOnStart): absurdalne parametry szacunku zatrzymują aplikację,
// zamiast dawać w planie np. przejazd w 0 minut.
public sealed class RoutingOptionsValidator : IValidateOptions<RoutingOptions>
{
    public ValidateOptionsResult Validate(string? name, RoutingOptions options)
    {
        var errors = GetErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> GetErrors(RoutingOptions options)
    {
        var errors = new List<string>();
        var estimate = options.Estimate;
        const string prefix = "Routing:Estimate";

        CheckRange(errors, $"{prefix}:DrivingDetourFactor", estimate.DrivingDetourFactor, 1, 3, "");
        CheckRange(errors, $"{prefix}:DrivingSpeedKmh", estimate.DrivingSpeedKmh, 5, 130, " km/h");
        CheckRange(errors, $"{prefix}:DrivingOverheadMinutes", estimate.DrivingOverheadMinutes, 0, 30, " min");
        CheckRange(errors, $"{prefix}:ParkingMinutes", estimate.ParkingMinutes, 0, 60, " min");
        CheckRange(errors, $"{prefix}:WalkingDetourFactor", estimate.WalkingDetourFactor, 1, 3, "");
        CheckRange(errors, $"{prefix}:WalkingSpeedKmh", estimate.WalkingSpeedKmh, 1, 8, " km/h");
        CheckRange(errors, $"{prefix}:MaxWalkingMinutes", estimate.MaxWalkingMinutes, 0, 120, " min");

        var matrix = options.Matrix;
        CheckRange(errors, "Routing:Matrix:DrivingMaxDistanceKm", matrix.DrivingMaxDistanceKm, 1, 300, " km");
        CheckRange(errors, "Routing:Matrix:WalkingMaxDistanceKm", matrix.WalkingMaxDistanceKm, 0.1, 20, " km");

        return errors;
    }

    private static void CheckRange(List<string> errors, string key, double value, double min, double max, string unit)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            errors.Add($"{key}: wartość od {min} do {max}{unit}.");
    }
}
