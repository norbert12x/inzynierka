using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class DayPlannerTests
{
    private static readonly FakeTravelTimes Travel = new();

    [Fact]
    public void OrdersAttractions_ToMinimiseTravel()
    {
        var a30 = Attraction("30", 30, 30);
        var a10 = Attraction("10", 10, 30);
        var a20 = Attraction("20", 20, 30);

        var plan = DayPlanner.Plan(Day(), [a30, a10, a20], Travel);

        Assert.Equal(new[] { a10, a20, a30 }, plan.Visits.Select(v => v.Attraction));
        Assert.Equal(60, plan.TravelMinutes);
        Assert.Empty(plan.Skipped);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void AllFits_ReturnBeforeEndOfDay_WithMeal()
    {
        var plan = DayPlanner.Plan(Day(meal: Meal()),
            [Attraction("A", 10, 120), Attraction("B", 20, 90), Attraction("C", 40, 60)], Travel);

        Assert.Equal(3, plan.Visits.Count);
        Assert.NotNull(plan.Meal);
        Assert.Equal(0, plan.OverrunMinutes);
        Assert.True(plan.ReturnArrival <= plan.Settings.ActiveTo);
    }

    [Fact]
    public void OptionalThatDoesNotFit_IsSkippedWithWarning()
    {
        // Wszystko: 40 min jazdy + 210 min wizyt = powrót 13:10 przy końcu dnia 12:00 (70 min > 30 tolerancji).
        // Usuwamy najkrótszą C – nadal 40 min za dużo – potem B; C wraca, bo A + C mieści się w dniu.
        var a = Attraction("A", 10, 60);
        var b = Attraction("B", 20, 120, mandatory: false);
        var c = Attraction("C", 15, 30, mandatory: false);

        var plan = DayPlanner.Plan(Day(to: "12:00"), [a, b, c], Travel);

        Assert.Equal(new[] { a, c }, plan.Visits.Select(v => v.Attraction));
        Assert.Equal(new[] { b }, plan.Skipped);
        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(PlanWarningCode.OptionalSkipped, warning.Code);
        Assert.Equal(b.PlaceId, warning.PlaceId);
        Assert.Equal(At("11:00"), plan.ReturnArrival);
    }

    [Fact]
    public void SmallOverrun_KeepsEverything_WithWarning()
    {
        // 20 min jazdy + 170 min wizyt = 10 min po końcu dnia – w tolerancji 30 min, decyzja należy do użytkownika.
        var a = Attraction("A", 10, 30);
        var b = Attraction("B", 10, 100, mandatory: false);
        var c = Attraction("C", 10, 40, mandatory: false);

        var plan = DayPlanner.Plan(Day(to: "12:00"), [a, b, c], Travel);

        Assert.Equal(3, plan.Visits.Count);
        Assert.Empty(plan.Skipped);
        Assert.Equal(10, plan.OverrunMinutes);
        Assert.Equal(PlanWarningCode.DayOverrun, Assert.Single(plan.Warnings).Code);
    }

    [Fact]
    public void ZeroTolerance_DropsShortestOptional()
    {
        var a = Attraction("A", 10, 30);
        var b = Attraction("B", 10, 100, mandatory: false);
        var c = Attraction("C", 10, 40, mandatory: false);

        var plan = DayPlanner.Plan(Day(to: "12:00"), [a, b, c], Travel, overrunToleranceMinutes: 0);

        Assert.Equal(new[] { c }, plan.Skipped);
        Assert.Equal(0, plan.OverrunMinutes);
    }

    [Fact]
    public void LargeOverrun_DropsShortestOptional_UntilWithinTolerance()
    {
        // 20 min jazdy + 205 min wizyt = 45 min za dużo; po usunięciu najkrótszej C (25 min) – 20 min, w tolerancji.
        var a = Attraction("A", 10, 80);
        var b = Attraction("B", 10, 100, mandatory: false);
        var c = Attraction("C", 10, 25, mandatory: false);

        var plan = DayPlanner.Plan(Day(to: "12:00"), [a, b, c], Travel);

        Assert.Equal(new[] { a, b }, plan.Visits.Select(v => v.Attraction));
        Assert.Equal(new[] { c }, plan.Skipped);
        Assert.Equal(20, plan.OverrunMinutes);
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.DayOverrun);
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.OptionalSkipped && w.PlaceId == c.PlaceId);
    }

    [Fact]
    public void MandatoryThatDoesNotFit_StaysInPlanWithWarning()
    {
        var a = Attraction("A", 10, 120);
        var b = Attraction("B", 5, 10, mandatory: false);

        var plan = DayPlanner.Plan(Day(to: "10:00"), [a, b], Travel);

        Assert.Equal(new[] { a }, plan.Visits.Select(v => v.Attraction));
        Assert.Equal(new[] { b }, plan.Skipped);
        Assert.True(plan.OverrunMinutes > 0);
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.DayOverrun);
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.OptionalSkipped && w.PlaceId == b.PlaceId);
    }

    [Fact]
    public void MandatoryAttractions_AreNeverSkipped()
    {
        var attractions = Enumerable.Range(1, 6).Select(i => Attraction($"M{i}", i * 10, 90)).ToList();

        var plan = DayPlanner.Plan(Day(), attractions, Travel);

        Assert.Equal(6, plan.Visits.Count);
        Assert.Empty(plan.Skipped);
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.DayOverrun);
    }

    [Fact]
    public void DifferentLodgings_DayEndsAtNewLodging()
    {
        var plan = DayPlanner.Plan(Day(startX: 0, endX: 50), [Attraction("A", 20, 30)], Travel);

        Assert.Equal(30, plan.ReturnLeg.Minutes);
        Assert.Equal(At("10:20"), plan.ReturnArrival); // 9:20–9:50, potem 30 min do nowego noclegu
    }

    [Fact]
    public void DuplicateAttraction_Throws()
    {
        var a = Attraction("A", 10, 30);

        Assert.Throws<ArgumentException>(() => DayPlanner.Plan(Day(), [a, a], Travel));
    }

    [Fact]
    public void EndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentException>(() => DayPlanner.Plan(Day(from: "18:00", to: "09:00"), [], Travel));
    }
}
