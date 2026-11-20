using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class TripFillerTests
{
    private static readonly FakeTravelTimes Travel = new();

    private static TripDay TripDay(int index, double lodgingX, string to = "18:00")
        => new(Date.AddDays(index), Lodging(lodgingX), At("09:00"), At(to), null);

    private static TripPlan Trip(IReadOnlyList<PlanAttraction> attractions, params TripDay[] days)
        => TripPlanner.Plan(days.Length > 0 ? days : [TripDay(0, 0)], attractions, Travel);

    private static SuggestionCandidate Candidate(string name, double x, int visitMinutes, string category = "museums",
        string? hours = "24/7")
        => new(Attraction(name, x, visitMinutes, mandatory: false, hours: hours), [category]);

    private static TripPlan Fill(TripPlan trip, IReadOnlyList<SuggestionCandidate> candidates, string[]? interests = null)
        => TripFiller.Fill(trip, candidates, interests ?? [], Travel);

    private static IEnumerable<string> Names(DayPlan day) => day.Visits.Select(v => v.Attraction.Name);

    [Fact]
    public void FillsDay_BigFirst_ThenSmallOnTheWay()
    {
        // Dzień 9:00–12:00, wybrana A (10, 60 min). Duża B1 (12) mieści się, B2 (30) już nie;
        // mała M (11) leży między A i B1 – 0 min objazdu.
        var a = Attraction("A", 10, 60);
        var trip = Trip([a], TripDay(0, 0, "12:00"));

        var filled = Fill(trip, [Candidate("B1", 12, 60), Candidate("B2", 30, 60), Candidate("M", 11, 10, "public-art")]);

        var day = filled.Days[0];
        Assert.Equal(["A", "M", "B1"], Names(day));
        Assert.False(day.Visits[0].Attraction.IsAddedByPlanner);
        Assert.All(day.Visits.Skip(1), v => Assert.True(v.Attraction.IsAddedByPlanner));
        Assert.Equal(0, day.OverrunMinutes);
    }

    [Fact]
    public void SmallAttraction_NotAddedWhenNotOnTheWay()
    {
        // Mnóstwo wolnego czasu, ale mural w 40 dokłada 60 min jazdy – nie jedziemy specjalnie do murala.
        var trip = Trip([Attraction("A", 10, 60)]);

        var filled = Fill(trip, [Candidate("Mural", 40, 10, "public-art")]);

        Assert.Equal(["A"], Names(filled.Days[0]));
    }

    [Fact]
    public void FillsUntilNoBigAttractionFits_NeverOverruns()
    {
        var candidates = Enumerable.Range(1, 12).Select(i => Candidate($"B{i}", i * 3, 60)).ToList();

        var filled = Fill(Trip([Attraction("A", 10, 60)]), candidates);

        var day = filled.Days[0];
        Assert.Equal(0, day.OverrunMinutes);
        Assert.True(day.Visits.Count >= 7); // 9:00–18:00: 8 wizyt po 60 min + dojazdy
        // Żadna z pozostałych dużych się nie mieści.
        var left = candidates.Where(c => day.Visits.All(v => v.Attraction.PlaceId != c.Attraction.PlaceId));
        Assert.All(left, c => Assert.True(
            DayScheduler.Schedule(day.Settings, [.. day.Visits.Select(v => v.Attraction), c.Attraction], Travel).OverrunMinutes > 0));
    }

    [Fact]
    public void OnlyMatchingInterests()
    {
        var filled = Fill(Trip([Attraction("A", 10, 60)]),
            [Candidate("Muzeum", 12, 60), Candidate("Park", 11, 60, "nature")], ["museums"]);

        Assert.Equal(["A", "Muzeum"], Names(filled.Days[0]));
    }

    [Fact]
    public void EmptyTripDays_FilledInTurns()
    {
        // Dwa dni 9:00–12:00 bez wybranych atrakcji; każda duża (120 min) mieści się tylko pojedynczo – po jednej na dzień.
        var trip = Trip([], TripDay(0, 0, "12:00"), TripDay(1, 0, "12:00"));

        var filled = Fill(trip, [Candidate("C1", 10, 120), Candidate("C2", 12, 120), Candidate("C3", -10, 120)]);

        Assert.All(filled.Days, d => Assert.Single(d.Visits));
        Assert.Equal(["C1"], Names(filled.Days[0]));
        Assert.Equal(["C3"], Names(filled.Days[1]));
    }

    [Fact]
    public void PlannedOrClosedOrOverrunDays_NotFilled()
    {
        var planned = Attraction("A", 10, 60);
        var trip = Trip([planned]);

        var filled = Fill(trip,
            [new(planned, ["museums"]), Candidate("Zamknięte", 12, 60, hours: "Mo-Fr 09:00-17:00")]); // sobota

        Assert.Equal(["A"], Names(filled.Days[0]));

        // Dzień już przekroczony (w tolerancji) – nic nie dokładamy.
        var full = Trip([Attraction("Długa", 10, 530)]); // powrót 18:10
        Assert.True(full.Days[0].OverrunMinutes > 0);
        Assert.Single(Fill(full, [Candidate("B", 11, 30)]).Days[0].Visits);
    }

    [Fact]
    public void KnownHours_PreferredOverUnknown()
    {
        // Dzień 9:00–11:00 mieści jedną dużą: bliższa ma nieznane godziny, dalsza – znane.
        var trip = Trip([], TripDay(0, 0, "11:00"));

        var filled = Fill(trip, [Candidate("Bez godzin", 5, 60, hours: null), Candidate("Z godzinami", 15, 60)]);

        Assert.Equal(["Z godzinami"], Names(filled.Days[0]));
    }
}
