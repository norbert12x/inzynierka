using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class RouteOptimizerTests
{
    private static readonly FakeTravelTimes Travel = new();

    [Fact]
    public void AttractionsOnLine_VisitedInOrderOfDistance()
    {
        var a30 = Attraction("30", 30, 0);
        var a10 = Attraction("10", 10, 0);
        var a20 = Attraction("20", 20, 0);

        var route = RouteOptimizer.Order(Lodging(0), Lodging(0), [a30, a10, a20], Travel);

        Assert.Equal(new[] { a10, a20, a30 }, route);
        Assert.Equal(60, RouteOptimizer.TravelMinutes(Lodging(0), Lodging(0), route, Travel));
    }

    [Fact]
    public void TwoOpt_ImprovesNearestNeighbourRoute()
    {
        // Najbliższy sąsiad: 0 → 2 → −3 → 10 → 0 = 30 min; najlepsza trasa w obie strony po linii = 26 min.
        var route = RouteOptimizer.Order(Lodging(0), Lodging(0),
            [Attraction("2", 2, 0), Attraction("-3", -3, 0), Attraction("10", 10, 0)], Travel);

        Assert.Equal(26, RouteOptimizer.TravelMinutes(Lodging(0), Lodging(0), route, Travel));
    }

    [Fact]
    public void DifferentEnd_RouteHeadsTowardsEnd()
    {
        // Nocleg na końcu dnia w punkcie 100: najpierw −10, potem 5 (120 min), nie 5 → −10 (130 min).
        var minus10 = Attraction("-10", -10, 0);
        var five = Attraction("5", 5, 0);

        var route = RouteOptimizer.Order(Lodging(0), Lodging(100), [five, minus10], Travel);

        Assert.Equal(new[] { minus10, five }, route);
        Assert.Equal(120, RouteOptimizer.TravelMinutes(Lodging(0), Lodging(100), route, Travel));
    }

    [Fact]
    public void Empty_ReturnsEmptyRoute()
    {
        Assert.Empty(RouteOptimizer.Order(Lodging(0), Lodging(0), [], Travel));
    }
}
