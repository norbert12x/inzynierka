using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class SuggestionFinderTests
{
    private static readonly FakeTravelTimes Travel = new();

    private static TripDay TripDay(int index, double lodgingX, string to = "18:00")
        => new(Date.AddDays(index), Lodging(lodgingX), At("09:00"), At(to), null);

    private static TripPlan Trip(IReadOnlyList<PlanAttraction> attractions, params TripDay[] days)
        => TripPlanner.Plan(days.Length > 0 ? days : [TripDay(0, 0)], attractions, Travel);

    private static SuggestionCandidate Candidate(string name, double x, int visitMinutes, string category = "museums",
        string? hours = "24/7")
        => new(Attraction(name, x, visitMinutes, mandatory: false, hours: hours), [category]);

    private static IReadOnlyList<PlanSuggestion> Find(TripPlan trip, IReadOnlyList<SuggestionCandidate> candidates,
        string[]? interests = null, int maxPerDay = SuggestionFinder.DefaultMaxPerDay)
        => SuggestionFinder.Find(trip, candidates, interests ?? [], Travel, maxPerDay);

    [Fact]
    public void OnlyCandidatesMatchingInterests()
    {
        var trip = Trip([Attraction("A", 10, 60)]);

        var suggestions = Find(trip, [Candidate("Muzeum", 12, 60), Candidate("Park", 11, 60, "nature")], ["museums"]);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Muzeum", suggestion.Attraction.Name);
        Assert.Equal(["museums"], suggestion.MatchedCategories);
    }

    [Fact]
    public void NoInterests_AnyCategory()
    {
        var trip = Trip([Attraction("A", 10, 60)]);

        Assert.Equal(2, Find(trip, [Candidate("Muzeum", 12, 60), Candidate("Park", 11, 60, "nature")]).Count);
    }

    [Fact]
    public void SuggestionsDoNotChangePlan()
    {
        var trip = Trip([Attraction("A", 10, 60)]);

        var suggestions = Find(trip, [Candidate("Muzeum", 12, 60)]);

        Assert.Single(suggestions);
        Assert.Equal(["A"], trip.Days[0].Visits.Select(v => v.Attraction.Name));
    }

    [Fact]
    public void InsertedWithoutChangingOrder_InCheapestPlace()
    {
        // Plan: 0 → A(10) → B(30) → 0; C w punkcie 20 leży po drodze – między A i B, 0 min jazdy więcej.
        var trip = Trip([Attraction("A", 10, 60), Attraction("B", 30, 30)]);

        var suggestion = Assert.Single(Find(trip, [Candidate("C", 20, 30)]));

        Assert.Equal(1, suggestion.InsertAt);
        Assert.Equal((At("10:20"), At("10:50")), (suggestion.Arrival, suggestion.Departure));
        Assert.Equal(0, suggestion.ExtraTravelMinutes);
        Assert.Equal(360, suggestion.FreeMinutesLeft); // powrót 12:00, koniec dnia 18:00
    }

    [Fact]
    public void NoFreeTime_NoSuggestions()
    {
        var trip = Trip([Attraction("A", 10, 450)], TripDay(0, 0, "17:00")); // powrót 16:50 – 10 min wolnego

        Assert.Empty(Find(trip, [Candidate("Muzeum", 10, 30)]));
    }

    [Fact]
    public void NearestFirst_LimitedPerDay()
    {
        var trip = Trip([Attraction("A", 10, 60)]);

        var suggestions = Find(trip,
            [Candidate("Daleko", 60, 30), Candidate("Blisko", 12, 30), Candidate("Średnio", 30, 30)], maxPerDay: 2);

        Assert.Equal(["Blisko", "Średnio"], suggestions.Select(s => s.Attraction.Name));
    }

    [Fact]
    public void EachCandidateOnce_InDayWithLeastExtraTravel()
    {
        // Dzień 1: nocleg 0; dzień 2: przeprowadzka 0 → 100. Kandydat w 80 leży po drodze dnia 2.
        var trip = Trip([], TripDay(0, 0), TripDay(1, 100));

        var suggestion = Assert.Single(Find(trip, [Candidate("Po drodze", 80, 30)]));

        Assert.Equal(Date.AddDays(1), suggestion.Date);
        Assert.Equal(0, suggestion.ExtraTravelMinutes);
    }

    [Fact]
    public void AttractionsAlreadyInPlanOrSkipped_NotSuggested()
    {
        var planned = Attraction("A", 10, 60);
        var skipped = Attraction("Za długa", 10, 600, mandatory: false);
        var trip = Trip([planned, skipped]);
        Assert.Equal([skipped], trip.Skipped);

        var suggestions = Find(trip, [new(planned, ["museums"]), new(skipped, ["museums"])]);

        Assert.Empty(suggestions);
    }

    [Fact]
    public void ClosedCandidate_NotSuggested()
    {
        var trip = Trip([Attraction("A", 10, 60)]); // sobota

        Assert.Empty(Find(trip, [Candidate("Muzeum", 12, 60, hours: "Mo-Fr 09:00-17:00")]));
    }

    [Fact]
    public void CandidateThatPushesOtherVisitPastClosing_NotSuggested()
    {
        // A jest otwarte do 10:30 – wizyta 9:10–10:10. Przed A i za A kandydat dokłada tyle samo jazdy (0 min),
        // ale przed A przesunąłby wizytę na 9:40–10:40, za zamknięcie – zostaje pozycja za A.
        var trip = Trip([Attraction("A", 10, 60, hours: "Sa 09:00-10:30")]);

        var suggestion = Assert.Single(Find(trip, [Candidate("C", 5, 30)]));

        Assert.Equal(1, suggestion.InsertAt);
    }

    [Fact]
    public void UnknownHours_SuggestedWithWarning()
    {
        var trip = Trip([Attraction("A", 10, 60)]);

        var suggestion = Assert.Single(Find(trip, [Candidate("Muzeum", 12, 60, hours: null)]));

        Assert.Equal(PlanWarningCode.OpeningHoursUnknown, Assert.Single(suggestion.Warnings).Code);
    }
}
