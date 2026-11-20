using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class TripPlannerTests
{
    private static readonly FakeTravelTimes Travel = new();

    private static TripDay TripDay(int index, double lodgingX, string to = "18:00")
        => new(Date.AddDays(index), Lodging(lodgingX), At("09:00"), At(to), null);

    private static IEnumerable<string> Names(DayPlan day) => day.Visits.Select(v => v.Attraction.Name).Order();

    [Fact]
    public void DifferentLodgings_AttractionsGoToDayPassingNearby()
    {
        // Dzień 1: nocleg 0 → 0; dzień 2: przeprowadzka 0 → 100; dzień 3: nocleg 100 → 100.
        var west = Attraction("W", -30, 60);
        var middle = Attraction("M", 50, 60);
        var east = Attraction("E", 130, 60);

        var trip = TripPlanner.Plan([TripDay(0, 0), TripDay(1, 100), TripDay(2, 100)], [west, middle, east], Travel);

        Assert.Equal(["W"], Names(trip.Days[0]));
        Assert.Equal(["M"], Names(trip.Days[1])); // po drodze między noclegami – 0 min objazdu
        Assert.Equal(["E"], Names(trip.Days[2]));
        Assert.Empty(trip.Skipped);
    }

    [Fact]
    public void NextDay_StartsAtPreviousLodging()
    {
        var trip = TripPlanner.Plan([TripDay(0, 0), TripDay(1, 100), TripDay(2, 100)], [], Travel);

        Assert.Equal((Lodging(0), Lodging(0)), (trip.Days[0].Settings.Start, trip.Days[0].Settings.End));
        Assert.Equal((Lodging(0), Lodging(100)), (trip.Days[1].Settings.Start, trip.Days[1].Settings.End));
        Assert.Equal((Lodging(100), Lodging(100)), (trip.Days[2].Settings.Start, trip.Days[2].Settings.End));
        Assert.Equal(At("10:40"), trip.Days[1].ReturnArrival); // 100 min przejazdu do nowego noclegu
    }

    [Fact]
    public void SameLodging_GroupsNearbyAttractionsIntoOneDay()
    {
        // Dzień 9:00–12:00: dwie atrakcje na wschodzie i dwie na zachodzie – każdy kierunek w jednym dniu.
        var trip = TripPlanner.Plan([TripDay(0, 0, "12:00"), TripDay(1, 0, "12:00")],
            [Attraction("E1", 30, 40), Attraction("W1", -30, 40), Attraction("E2", 35, 40), Attraction("W2", -35, 40)],
            Travel);

        var groups = trip.Days.Select(d => string.Join(",", Names(d))).Order();
        Assert.Equal(["E1,E2", "W1,W2"], groups);
        Assert.All(trip.Days, d => Assert.Equal(0, d.OverrunMinutes));
    }

    [Fact]
    public void FullDay_AttractionGoesToNextDay()
    {
        // Dzień 9:00–13:00 nie mieści dwóch wizyt po 150 min.
        var trip = TripPlanner.Plan([TripDay(0, 0, "13:00"), TripDay(1, 0, "13:00")],
            [Attraction("A", 10, 150), Attraction("B", -10, 150)], Travel);

        Assert.Equal(["A"], Names(trip.Days[0]));
        Assert.Equal(["B"], Names(trip.Days[1]));
        Assert.All(trip.Days, d => Assert.Empty(d.Warnings));
    }

    [Fact]
    public void ThreeDays_AttractionsSpreadOverAllDays()
    {
        // 9 atrakcji po 90 min w jednym kierunku: zmieściłoby się 5 + 4 w dwóch dniach, ale kara za obciążenie
        // rozkłada je po 3 na dzień – sąsiednie razem (najdalsze w dniu 1, środkowe w 2, najbliższe w 3 i 2).
        var attractions = Enumerable.Range(1, 9).Select(i => Attraction($"A{i}", i * 5, 90)).ToList();

        var trip = TripPlanner.Plan([TripDay(0, 0), TripDay(1, 0), TripDay(2, 0)], attractions, Travel);

        Assert.Equal([3, 3, 3], trip.Days.Select(d => d.Visits.Count));
        Assert.Equal(["A7", "A8", "A9"], Names(trip.Days[0]));
        Assert.All(trip.Days, d => Assert.Equal(0, d.OverrunMinutes));
    }

    [Fact]
    public void NearbyAttractions_StayTogetherDespiteLoad()
    {
        // Dwie atrakcje obok siebie daleko od noclegu: druga dokłada 0 min jazdy do dnia pierwszej
        // (kara 0,25 × 60 = 15 min), a do pustego dnia 100 min – zostają razem.
        var trip = TripPlanner.Plan([TripDay(0, 0), TripDay(1, 0)],
            [Attraction("A", 50, 60), Attraction("B", 51, 60)], Travel);

        Assert.Equal(["A", "B"], Names(trip.Days[0]));
        Assert.Empty(trip.Days[1].Visits);
    }

    [Fact]
    public void MandatoryThatFitsNowhere_StaysWithOverrunWarnings()
    {
        var trip = TripPlanner.Plan([TripDay(0, 0, "10:00"), TripDay(1, 0, "10:00")],
            [Attraction("A", 10, 120), Attraction("B", 10, 120)], Travel);

        Assert.All(trip.Days, d => Assert.Single(d.Visits));
        Assert.All(trip.Days, d => Assert.Contains(d.Warnings, w => w.Code == PlanWarningCode.DayOverrun));
        Assert.Empty(trip.Skipped);
    }

    [Fact]
    public void OptionalWithoutRoomInAnyDay_ReportedOnceForTrip()
    {
        // Jeden dzień 9:00–11:00: A (obowiązkowa) mieści się, B przekracza dzień o 90 min (> 30 tolerancji).
        var b = Attraction("B", 10, 100, mandatory: false);

        var trip = TripPlanner.Plan([TripDay(0, 0, "11:00")], [Attraction("A", 10, 90), b], Travel);

        Assert.Equal([b], trip.Skipped);
        var warning = Assert.Single(trip.Warnings);
        Assert.Equal(PlanWarningCode.OptionalSkipped, warning.Code);
        Assert.Null(warning.Date);
        Assert.Empty(trip.Days[0].Skipped);
        Assert.Empty(trip.Days[0].Warnings);
    }

    [Fact]
    public void DatesNotAscending_Throws()
    {
        Assert.Throws<ArgumentException>(() => TripPlanner.Plan([TripDay(1, 0), TripDay(0, 0)], [], Travel));
        Assert.Throws<ArgumentException>(() => TripPlanner.Plan([], [], Travel));
    }

    [Fact]
    public void DuplicateAttraction_Throws()
    {
        var a = Attraction("A", 10, 30);

        Assert.Throws<ArgumentException>(() => TripPlanner.Plan([TripDay(0, 0), TripDay(1, 0)], [a, a], Travel));
    }
}
