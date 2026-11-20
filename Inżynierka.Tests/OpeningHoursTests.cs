using Inżynierka.Planning;
using static Inżynierka.Tests.PlanningTestData;

namespace Inżynierka.Tests;

// Zapisy z katalogu (20.11). Daty: 15.05.2027 – sobota, 2.05.2027 – pierwsza niedziela maja.
public class OpeningHoursTests
{
    private static readonly DateOnly Saturday = new(2027, 5, 15);
    private static readonly DateOnly Monday = new(2027, 5, 17);
    private static readonly DateOnly Thursday = new(2027, 5, 20);
    private static readonly DateOnly FirstSunday = new(2027, 5, 2);
    private static readonly DateOnly SecondSunday = new(2027, 5, 9);
    private static readonly DateOnly NovemberSunday = new(2027, 11, 7);

    private static string Intervals(string text, DateOnly date)
        => string.Join(", ", OpeningHours.TryParse(text)!.IntervalsOn(date));

    [Fact]
    public void TestDates_HaveExpectedWeekdays()
    {
        Assert.Equal(DayOfWeek.Saturday, Saturday.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, Monday.DayOfWeek);
        Assert.Equal(DayOfWeek.Thursday, Thursday.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, FirstSunday.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, NovemberSunday.DayOfWeek);
    }

    [Theory]
    [InlineData("Tu-Su 10:00-18:00", "2027-05-15", "10:00–18:00")]
    [InlineData("Tu-Su 10:00-18:00", "2027-05-17", "")]
    [InlineData("Mo-Fr 09:45-15:00; Sa-Su 09:45-17:00", "2027-05-15", "09:45–17:00")]
    [InlineData("Tu-We, Fr 08:30-15:30; Th 08:30-17:30; Sa-Su 14:00-19:00", "2027-05-20", "08:30–17:30")]
    [InlineData("Mo,Sa off; Tu-Th 08:30-15:30; Fr 08:30-17:30", "2027-05-15", "")]
    [InlineData("Mo off; Tu-Su 15:30-18:30", "2027-05-17", "")]
    [InlineData("May-Aug Tu-Fr 12:00-17:00; May-Aug Su 14:00-17:00; Mo Off", "2027-05-20", "12:00–17:00")]
    [InlineData("May-Aug Tu-Fr 12:00-17:00; May-Aug Su 14:00-17:00; Mo Off", "2027-11-07", "")]
    [InlineData("May-Oct Su 11:00-18:00; Nov-Apr Su 11:00-16:00", "2027-11-07", "11:00–16:00")]
    [InlineData("Apr-Oct Mo-Fr 09:00-17:00; Sa-Su,PH 10:00-18:00; Nov-Mar Mo-Su,PH 09:00-16:00", "2027-05-15", "10:00–18:00")]
    [InlineData("Apr-Oct Mo-Fr 09:00-17:00; Sa-Su,PH 10:00-18:00; Nov-Mar Mo-Su,PH 09:00-16:00", "2027-11-07", "09:00–16:00")]
    [InlineData("Su[1] 8:30-12:00", "2027-05-02", "08:30–12:00")]
    [InlineData("Su[1] 8:30-12:00", "2027-05-09", "")]
    [InlineData("Su[-1] 10:00-12:00", "2027-05-30", "10:00–12:00")]
    [InlineData("09:00-19:00", "2027-05-17", "09:00–19:00")]
    [InlineData("Mo-Fr 08:00-12:00, 13:00-16:00", "2027-05-17", "08:00–12:00, 13:00–16:00")]
    [InlineData("Fr-Sa 18:00-02:00", "2027-05-15", "18:00–24:00")]
    [InlineData("24/7", "2027-05-15", "00:00–24:00")]
    public void ReadableHours_GiveIntervalsForDate(string text, string date, string expected)
    {
        Assert.Equal(expected, Intervals(text, DateOnly.Parse(date)));
    }

    [Theory]
    [InlineData("Jan-Mar;Nov-Dec Tu-Su 10:00-16:00; Apr-Oct Tu-Su 10:00-18:00; Nov 1 off; Nov 11 off")]
    [InlineData("Dec 25-Feb 2: Mo-Sa 15:00-18:00; Dec 25-Feb 2:Su 09:00-18:00; PH Dec 25-Feb 2:09:00-18:00")]
    [InlineData("Mo-Fr")]
    [InlineData("Mo-Fr 09:00-17:00 \"po umówieniu\"")]
    [InlineData("Mo-Fr 09:00-17:00 || \"po umówieniu\"")]
    [InlineData("Mo-Fr 25:00-26:00")]
    [InlineData("PH off")]
    [InlineData("")]
    public void UnsupportedHours_AreUnreadable(string text)
    {
        Assert.Null(OpeningHours.TryParse(text));
    }

    [Fact]
    public void LaterRule_OverridesEarlierForSameDays()
    {
        Assert.Equal("10:00–12:00", Intervals("Mo-Sa 09:00-17:00; Sa 10:00-12:00", Saturday));
        Assert.Equal("09:00–17:00", Intervals("Mo-Sa 09:00-17:00; Sa 10:00-12:00", Monday));
    }

    // --- Ostrzeżenia w planie dnia (sobota 15.05.2027, 9:00–18:00, wizyta A 9:10–10:10) ---

    private static IEnumerable<PlanWarning> HoursWarnings(string? hours, bool hasEntrance = true)
        => DayScheduler.Schedule(Day(), [Attraction("A", 10, 60, hours: hours, hasEntrance: hasEntrance)], new FakeTravelTimes())
            .Warnings.Where(w => w.Code != PlanWarningCode.DayOverrun);

    [Fact]
    public void NoHours_WithEntrance_WarnsUnknown()
    {
        var warning = Assert.Single(HoursWarnings(null));
        Assert.Equal(PlanWarningCode.OpeningHoursUnknown, warning.Code);
        Assert.Equal(Date, warning.Date);
    }

    [Fact]
    public void NoHours_WithoutEntrance_NoWarning()
    {
        Assert.Empty(HoursWarnings(null, hasEntrance: false));
    }

    [Fact]
    public void KnownHours_WithoutEntrance_StillChecked()
    {
        // Np. park z godzinami – zamknięty w sobotę.
        Assert.Equal(PlanWarningCode.MaybeClosed,
            Assert.Single(HoursWarnings("Mo-Fr 06:00-22:00", hasEntrance: false)).Code);
    }

    [Theory]
    [InlineData(new[] { "museums" }, true)]
    [InlineData(new[] { "heritage", "museums" }, true)]
    [InlineData(new[] { "active-entertainment" }, true)]
    [InlineData(new[] { "heritage" }, false)]
    [InlineData(new[] { "nature", "public-art" }, false)]
    [InlineData(new string[0], false)]
    public void EntranceCategories_AnyCategoryWithEntrance(string[] codes, bool expected)
    {
        Assert.Equal(expected, EntranceCategories.HasEntrance(codes));
    }

    [Fact]
    public void VisitWithinHours_NoWarning()
    {
        Assert.Empty(HoursWarnings("Mo-Sa 09:00-17:00"));
    }

    [Fact]
    public void ClosedThatDay_WarnsMaybeClosed()
    {
        var warning = Assert.Single(HoursWarnings("Mo-Fr 09:00-17:00"));
        Assert.Equal(PlanWarningCode.MaybeClosed, warning.Code);
        Assert.Contains("sobota", warning.Message);
    }

    private static PlanAttraction Visit(string hours, int visitMinutes = 60) => Attraction("A", 10, visitMinutes, hours: hours);

    [Fact]
    public void ArrivalBeforeOpening_WaitsWithoutWarning()
    {
        // Przyjazd 9:10, otwarcie 10:00 – 50 min czekania, zwiedzanie 10:00–11:00.
        var plan = DayScheduler.Schedule(Day(), [Visit("Sa 10:00-18:00")], new FakeTravelTimes());

        var visit = Assert.Single(plan.Visits);
        Assert.Equal((At("09:10"), 50, At("11:00")), (visit.Arrival, visit.WaitMinutes, visit.Departure));
        Assert.Equal(At("11:10"), plan.ReturnArrival);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ArrivalDuringBreak_WaitsForAfternoonOpening()
    {
        // Przerwa 12:00–13:00: A trwa 9:10–12:30 bez czekania, B (dojazd 10 min) przyjeżdża 12:40 i czeka 20 min.
        var plan = DayScheduler.Schedule(Day(),
            [Attraction("A", 10, 200), Attraction("B", 20, 60, hours: "Sa 09:00-12:00,13:00-17:00")], new FakeTravelTimes());

        Assert.Equal((At("12:40"), 20, At("14:00")), (plan.Visits[1].Arrival, plan.Visits[1].WaitMinutes, plan.Visits[1].Departure));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void WaitLongerThanLimit_NoWaitAndWarning()
    {
        // Otwarcie 16:00 – 6 h 50 min po przyjeździe, więcej niż limit 60 min.
        var plan = DayScheduler.Schedule(Day(), [Visit("Sa 16:00-18:00")], new FakeTravelTimes());

        Assert.Equal(0, plan.Visits[0].WaitMinutes);
        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(PlanWarningCode.MaybeClosed, warning.Code);
        Assert.Contains("09:10–10:10", warning.Message);
        Assert.Contains("16:00–18:00", warning.Message);
    }

    [Fact]
    public void AfterClosingOrClosedDay_NoWait()
    {
        Assert.Equal(0, DayScheduler.Schedule(Day(from: "17:00", to: "22:00"), [Visit("Sa 09:00-17:00")], new FakeTravelTimes())
            .Visits[0].WaitMinutes);
        Assert.Equal(0, DayScheduler.Schedule(Day(), [Visit("Mo-Fr 09:00-17:00")], new FakeTravelTimes()).Visits[0].WaitMinutes);
        Assert.Equal(0, DayScheduler.Schedule(Day(), [Attraction("A", 10, 60, hours: null)], new FakeTravelTimes()).Visits[0].WaitMinutes);
    }

    [Fact]
    public void WaitCountsForMealDecision()
    {
        // A 9:10–10:10, B przyjeżdża 10:20, czeka do 11:00 i zwiedza 180 min → koniec 14:00 > 13:30 (najpóźniejszy start);
        // dlatego posiłek przed B, gdy tylko okno się otworzy (11:00–12:00), a B od 12:10 bez czekania.
        var plan = DayScheduler.Schedule(Day(meal: Meal(earliest: "11:00", latest: "13:30")),
            [Attraction("A", 10, 60), Attraction("B", 20, 180, hours: "Sa 11:00-18:00")], new FakeTravelTimes());

        Assert.Equal(new PlannedMeal(At("11:00"), At("12:00"), 1), plan.Meal);
        Assert.Equal((At("12:10"), 0), (plan.Visits[1].Arrival, plan.Visits[1].WaitMinutes));
    }

    [Fact]
    public void UnreadableHours_WarnsToCheck()
    {
        var warning = Assert.Single(HoursWarnings("Nov 1 off"));
        Assert.Equal(PlanWarningCode.OpeningHoursUnreadable, warning.Code);
    }

    [Fact]
    public void HoursWarning_DoesNotChangeOrder()
    {
        var closed = Attraction("Zamknięte", 10, 60, hours: "Mo-Fr 09:00-17:00");
        var open = Attraction("Otwarte", 20, 60);

        var plan = DayPlanner.Plan(Day(), [open, closed], new FakeTravelTimes());

        Assert.Equal(new[] { closed, open }, plan.Visits.Select(v => v.Attraction));
        Assert.Contains(plan.Warnings, w => w.Code == PlanWarningCode.MaybeClosed && w.PlaceId == closed.PlaceId);
    }
}
