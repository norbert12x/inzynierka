using Inżynierka.Configuration;
using Microsoft.Extensions.Configuration;

namespace Inżynierka.Tests;

public class CatalogOptionsValidatorTests
{
    private static RegionDefinition Region(string code = "rzeszow", bool isDefault = true) => new()
    {
        Code = code,
        Name = "Rzeszów i okolice",
        CenterLatitude = 50.037,
        CenterLongitude = 22.004,
        RadiusKm = 30,
        IsDefault = isDefault
    };

    private static IReadOnlyList<string> Errors(params RegionDefinition[] regions) =>
        CatalogOptionsValidator.GetErrors(new CatalogOptions { Regions = [.. regions] });

    [Fact]
    public void ValidSingleRegion_HasNoErrors()
    {
        Assert.Empty(Errors(Region()));
    }

    [Fact]
    public void TwoRegionsWithOneDefault_HasNoErrors()
    {
        Assert.Empty(Errors(Region(), Region("bieszczady", isDefault: false)));
    }

    [Fact]
    public void NoRegions_IsError()
    {
        Assert.Single(Errors());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DefaultMustBeExactlyOne(bool firstDefault, bool secondDefault)
    {
        var errors = Errors(Region("a", firstDefault), Region("b", secondDefault));

        Assert.Contains(errors, e => e.Contains("IsDefault"));
    }

    [Fact]
    public void DuplicateCode_IsError()
    {
        var errors = Errors(Region("rzeszow"), Region("rzeszow", isDefault: false));

        Assert.Contains(errors, e => e.Contains("powtórzone kody"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Rzeszow")]
    [InlineData("rzeszów")]
    [InlineData("rzeszow okolice")]
    [InlineData("-rzeszow")]
    public void InvalidCode_IsError(string code)
    {
        Assert.Contains(Errors(Region(code)), e => e.Contains(":Code:"));
    }

    [Theory]
    [InlineData(91, 22, 30, ":CenterLatitude:")]
    [InlineData(50, 181, 30, ":CenterLongitude:")]
    [InlineData(50, 22, 0, ":RadiusKm:")]
    [InlineData(50, 22, 101, ":RadiusKm:")]
    [InlineData(double.NaN, 22, 30, ":CenterLatitude:")]
    public void OutOfRangeValues_AreErrors(double latitude, double longitude, double radiusKm, string field)
    {
        var region = Region();
        region.CenterLatitude = latitude;
        region.CenterLongitude = longitude;
        region.RadiusKm = radiusKm;

        Assert.Contains(Errors(region), e => e.Contains(field));
    }

    // Chroni przed literówką w prawdziwym appsettings.json, który aplikacja czyta przy starcie.
    [Fact]
    public void AppSettingsJson_HasValidCatalogSection()
    {
        var appSettings = Path.Combine(FindSolutionDirectory(), "Inżynierka", "appsettings.json");
        var configuration = new ConfigurationBuilder().AddJsonFile(appSettings).Build();
        var options = configuration.GetSection(CatalogOptions.SectionName).Get<CatalogOptions>() ?? new CatalogOptions();

        Assert.Empty(CatalogOptionsValidator.GetErrors(options));
        Assert.Contains(options.Regions, r => r.Code == "rzeszow" && r.IsDefault);
    }

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Inżynierka.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Nie znaleziono Inżynierka.sln.");
    }
}
