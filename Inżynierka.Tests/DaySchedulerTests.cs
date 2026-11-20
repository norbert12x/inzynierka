using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

public class DaySchedulerTests
{
    private static readonly FakeTravelTimes Travel = new();

    [Fact]
    public void Times_FollowTravelAndVisits()
    {
        var plan = DayScheduler.Schedule(Day(), [Attraction("A", 10, 60), Attraction("B", 30, 30)], Travel);

        Assert.Equal((At("09:10"), At("10:10")), (plan.Visits[0].Arrival, plan.Visits[0].Departure));
        Assert.Equal((At("10:30"), At("11:00")), (plan.Visits[1].Arrival, plan.Visits[1].Departure));
        Assert.Equal(At("11:30"), plan.ReturnArrival);
        Assert.Equal(60, plan.TravelMinutes);
        Assert.Equal(0, plan.OverrunMinutes);
        Assert.Null(plan.Meal);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Meal_RightAfterVisit_WhenWindowAlreadyOpen()
    {
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 180), Attraction("B", 30, 30)], Travel);

        Assert.Equal(new PlannedMeal(At("12:10"), At("13:10"), 1), plan.Meal);
        Assert.Equal(At("13:30"), plan.Visits[1].Arrival);
        Assert.Equal(At("14:30"), plan.ReturnArrival);
    }

    [Fact]
    public void Meal_WaitsForWindow_WhenNextVisitWouldEndAfterLatestStart()
    {
        // Po A jest 10:10; B (dojazd 10 + 240 min) skończyłoby się o 14:20, po najpóźniejszym starcie 14:00.
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 60), Attraction("B", 20, 240)], Travel);

        Assert.Equal(new PlannedMeal(At("12:00"), At("13:00"), 1), plan.Meal);
        Assert.Equal(At("13:10"), plan.Visits[1].Arrival);
        Assert.Equal(At("17:30"), plan.ReturnArrival);
    }

    [Fact]
    public void Meal_AfterLastVisit_WhenWindowOpen()
    {
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 200)], Travel);

        Assert.Equal(new PlannedMeal(At("12:30"), At("13:30"), 1), plan.Meal);
        Assert.Equal(At("13:40"), plan.ReturnArrival);
    }

    [Fact]
    public void NoMeal_WhenSightseeingEndsBeforeWindow()
    {
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 60)], Travel);

        Assert.Null(plan.Meal);
        Assert.Equal(At("10:20"), plan.ReturnArrival);
    }

    [Fact]
    public void NoMeal_WhenDayStartsAfterWindow()
    {
        var plan = DayScheduler.Schedule(Day(from: "15:00", to: "22:00", meal: Meal()), [Attraction("A", 10, 300)], Travel);

        Assert.Null(plan.Meal);
    }

    [Fact]
    public void FixedMealTime_WaitsForIt()
    {
        // Posiłek dokładnie o 12:00 (okno 12:00–12:00): B po A skończyłoby się o 12:20.
        var plan = DayScheduler.Schedule(Day(meal: Meal(earliest: "12:00", latest: "12:00")),
            [Attraction("A", 10, 60), Attraction("B", 20, 120)], Travel);

        Assert.Equal(new PlannedMeal(At("12:00"), At("13:00"), 1), plan.Meal);
        Assert.Equal(At("13:10"), plan.Visits[1].Arrival);
    }

    [Fact]
    public void UserPlacedMeal_AfterChosenVisit_IgnoresWindow()
    {
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 60), Attraction("B", 30, 30)], Travel,
            mealAfterVisits: 2);

        Assert.Equal(new PlannedMeal(At("11:00"), At("12:00"), 2), plan.Meal);
        Assert.Equal(At("12:30"), plan.ReturnArrival);
    }

    [Fact]
    public void UserPlacedMeal_BeforeFirstVisit()
    {
        var plan = DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 60)], Travel, mealAfterVisits: 0);

        Assert.Equal(new PlannedMeal(At("09:00"), At("10:00"), 0), plan.Meal);
        Assert.Equal(At("10:10"), plan.Visits[0].Arrival);
    }

    [Fact]
    public void UserPlacedMeal_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DayScheduler.Schedule(Day(meal: Meal()), [Attraction("A", 10, 60)], Travel, mealAfterVisits: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DayScheduler.Schedule(Day(), [Attraction("A", 10, 60)], Travel, mealAfterVisits: 1));
    }

    [Fact]
    public void DefaultMeal_IsOneHour_Starting1330To1500()
    {
        Assert.Equal(new MealBreakSettings(60, At("13:30"), At("15:00")), MealBreakSettings.Default);
    }

    [Fact]
    public void Overrun_KeepsOrderAndWarns()
    {
        var a = Attraction("A", 10, 120);
        var b = Attraction("B", 5, 10);

        var plan = DayScheduler.Schedule(Day(to: "10:00"), [a, b], Travel);

        Assert.Equal(new[] { a, b }, plan.Visits.Select(v => v.Attraction));
        // A 9:10–11:10, B 11:15–11:25, powrót 11:30 – 90 min po 10:00.
        Assert.Equal(At("11:30"), plan.ReturnArrival);
        Assert.Equal(90, plan.OverrunMinutes);
        Assert.Equal(PlanWarningCode.DayOverrun, Assert.Single(plan.Warnings).Code);
    }

    [Fact]
    public void EmptyDay_GoesStraightToEnd()
    {
        var plan = DayScheduler.Schedule(Day(endX: 30), [], Travel);

        Assert.Empty(plan.Visits);
        Assert.Equal(30, plan.ReturnLeg.Minutes);
        Assert.Equal(At("09:30"), plan.ReturnArrival);
    }
}
