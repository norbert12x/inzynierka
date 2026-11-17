using Inżynierka.Planning;

namespace Inżynierka.Tests;

public class VisitDurationTests
{
    [Fact]
    public void PlaceMinutes_WinOverCategories()
    {
        var result = VisitDuration.Resolve(180, [90, 30], fallbackMinutes: 30);

        Assert.Equal(new VisitDuration(180, VisitMinutesSource.Place), result);
    }

    [Fact]
    public void WithoutPlaceMinutes_TakesLongestCategory()
    {
        // Muzeum (90) w zabytku (30) – liczymy czas muzeum.
        var result = VisitDuration.Resolve(null, [30, 90], fallbackMinutes: 30);

        Assert.Equal(new VisitDuration(90, VisitMinutesSource.CategoryDefault), result);
    }

    [Fact]
    public void WithoutCategories_UsesFallback()
    {
        var result = VisitDuration.Resolve(null, [], fallbackMinutes: 30);

        Assert.Equal(new VisitDuration(30, VisitMinutesSource.CategoryDefault), result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositivePlaceMinutes_AreIgnored(int placeMinutes)
    {
        var result = VisitDuration.Resolve(placeMinutes, [60], fallbackMinutes: 30);

        Assert.Equal(new VisitDuration(60, VisitMinutesSource.CategoryDefault), result);
    }
}
