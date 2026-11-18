using Inżynierka.Configuration;
using Inżynierka.Planning;
using Microsoft.Extensions.Configuration;

namespace Inżynierka.Tests;

public class RoutingOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.Empty(RoutingOptionsValidator.GetErrors(new RoutingOptions()));
    }

    [Theory]
    [InlineData(nameof(TravelEstimateSettings.DrivingDetourFactor), 0.9)]
    [InlineData(nameof(TravelEstimateSettings.DrivingSpeedKmh), 0.0)]
    [InlineData(nameof(TravelEstimateSettings.DrivingSpeedKmh), double.NaN)]
    [InlineData(nameof(TravelEstimateSettings.ParkingMinutes), -1.0)]
    [InlineData(nameof(TravelEstimateSettings.WalkingDetourFactor), 3.5)]
    [InlineData(nameof(TravelEstimateSettings.WalkingSpeedKmh), 20.0)]
    [InlineData(nameof(TravelEstimateSettings.MaxWalkingMinutes), 121.0)]
    public void OutOfRangeValue_IsError(string field, double value)
    {
        var options = new RoutingOptions();
        var estimate = options.Estimate;
        switch (field)
        {
            case nameof(estimate.DrivingDetourFactor): estimate.DrivingDetourFactor = value; break;
            case nameof(estimate.DrivingSpeedKmh): estimate.DrivingSpeedKmh = value; break;
            case nameof(estimate.ParkingMinutes): estimate.ParkingMinutes = (int)value; break;
            case nameof(estimate.WalkingDetourFactor): estimate.WalkingDetourFactor = value; break;
            case nameof(estimate.WalkingSpeedKmh): estimate.WalkingSpeedKmh = value; break;
            case nameof(estimate.MaxWalkingMinutes): estimate.MaxWalkingMinutes = (int)value; break;
        }

        var errors = RoutingOptionsValidator.GetErrors(options);

        Assert.Single(errors);
        Assert.Contains($"Routing:Estimate:{field}:", errors[0]);
    }

    [Theory]
    [InlineData(0.5, 3.0, "Routing:Matrix:DrivingMaxDistanceKm:")]
    [InlineData(80.0, 0.0, "Routing:Matrix:WalkingMaxDistanceKm:")]
    [InlineData(80.0, double.NaN, "Routing:Matrix:WalkingMaxDistanceKm:")]
    public void MatrixOutOfRange_IsError(double drivingKm, double walkingKm, string field)
    {
        var options = new RoutingOptions();
        options.Matrix.DrivingMaxDistanceKm = drivingKm;
        options.Matrix.WalkingMaxDistanceKm = walkingKm;

        var error = Assert.Single(RoutingOptionsValidator.GetErrors(options));
        Assert.StartsWith(field, error);
    }

    // Chroni przed literówką w prawdziwym appsettings.json, który aplikacja czyta przy starcie.
    [Fact]
    public void AppSettingsJson_HasValidRoutingSection()
    {
        var appSettings = Path.Combine(FindSolutionDirectory(), "Inżynierka", "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(appSettings).Build();
        var section = configuration.GetSection(RoutingOptions.SectionName);
        var options = section.Get<RoutingOptions>() ?? new RoutingOptions();

        Assert.True(section.GetSection("Estimate").Exists());
        Assert.True(section.GetSection("Matrix").Exists());
        Assert.Empty(RoutingOptionsValidator.GetErrors(options));
    }

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inżynierka.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Nie znaleziono Inżynierka.sln.");
    }
}
