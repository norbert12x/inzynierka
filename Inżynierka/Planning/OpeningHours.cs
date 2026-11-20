using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Inżynierka.Planning;

// Przedział otwarcia w minutach od północy (koniec 1440 = 24:00).
public readonly record struct OpenInterval(int FromMinute, int ToMinute)
{
    public override string ToString() => $"{Format(FromMinute)}–{Format(ToMinute)}";

    public static string Format(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";
}

// Prosty podzbiór formatu OSM opening_hours (bez bibliotek): reguły rozdzielone „;”, w każdej
// opcjonalnie miesiące (Jan, May-Aug, Nov-Feb), opcjonalnie dni tygodnia (Mo, Tu-Fr, Mo,Sa, Su[1] – pierwsza
// niedziela miesiąca, [-1] – ostatnia; PH/SH pomijane, bo nie znamy świąt), na końcu godziny
// (09:00-17:00, 8:30-12:00,13:00-16:00) albo off/closed; „24/7” – zawsze. Późniejsza reguła zastępuje
// wcześniejszą dla dni, które obejmuje (jak w OSM); dzień bez pasującej reguły – zamknięte.
// Czego nie umiemy odczytać (konkretne daty, święta bez dni, komentarze, „||”, reguła bez godzin) →
// TryParse zwraca null i planer ostrzega, że godziny trzeba sprawdzić. Nie zgadujemy.
public sealed partial class OpeningHours
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
    // Kolejność jak DayOfWeek: niedziela = 0.
    private static readonly string[] Weekdays = ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
    private static readonly ConcurrentDictionary<string, OpeningHours?> Cache = new();

    private readonly IReadOnlyList<Rule> _rules;

    private OpeningHours(IReadOnlyList<Rule> rules) => _rules = rules;

    // Wynik zapamiętujemy – planer sprawdza te same atrakcje przy każdym przeliczeniu.
    public static OpeningHours? TryParse(string text) => Cache.GetOrAdd(text.Trim(), Parse);

    // Przedziały otwarcia w danym dniu; pusta lista – zamknięte.
    public IReadOnlyList<OpenInterval> IntervalsOn(DateOnly date)
        => _rules.LastOrDefault(r => r.Matches(date))?.Intervals ?? [];

    private static OpeningHours? Parse(string text)
    {
        if (text.Length == 0)
            return null;
        if (text == "24/7")
            return new OpeningHours([new Rule(null, null, [new OpenInterval(0, 1440)])]);

        var rules = new List<Rule>();
        foreach (var part in text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var rule = ParseRule(part);
            if (rule is null)
                return null;
            if (rule.Weekdays is { Count: 0 })
                continue; // tylko PH/SH – nie znamy dat świąt
            rules.Add(rule);
        }
        return rules.Count > 0 ? new OpeningHours(rules) : null;
    }

    private static Rule? ParseRule(string text)
    {
        var tokens = CommaSpaces().Replace(text, ",").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is 0 or > 3)
            return null;

        var intervals = ParseTimes(tokens[^1]);
        if (intervals is null)
            return null;

        bool[]? months = null;
        List<WeekdayItem>? weekdays = null;
        var index = 0;
        if (index < tokens.Length - 1 && ParseMonths(tokens[index]) is { } parsedMonths)
        {
            months = parsedMonths;
            index++;
        }
        if (index < tokens.Length - 1)
        {
            weekdays = ParseWeekdays(tokens[index]);
            if (weekdays is null)
                return null;
            index++;
        }
        return index == tokens.Length - 1 ? new Rule(months, weekdays, intervals) : null;
    }

    private static List<OpenInterval>? ParseTimes(string token)
    {
        if (token.Equals("off", StringComparison.OrdinalIgnoreCase) || token.Equals("closed", StringComparison.OrdinalIgnoreCase))
            return [];

        var result = new List<OpenInterval>();
        foreach (var range in token.Split(','))
        {
            var match = TimeRange().Match(range);
            if (!match.Success)
                return null;
            var from = Minutes(match.Groups[1].Value, match.Groups[2].Value);
            var to = Minutes(match.Groups[3].Value, match.Groups[4].Value);
            if (from is null || to is null || from == to)
                return null;
            // Po północy (np. 18:00-02:00) – liczymy tylko do 24:00 tego dnia.
            result.Add(new OpenInterval(from.Value, to.Value < from.Value ? 1440 : to.Value));
        }
        return result;
    }

    private static int? Minutes(string hours, string minutes)
    {
        var h = int.Parse(hours);
        var m = int.Parse(minutes);
        return m < 60 && (h < 24 || h == 24 && m == 0) ? h * 60 + m : null;
    }

    private static bool[]? ParseMonths(string token)
    {
        var result = new bool[12];
        foreach (var item in token.Split(','))
        {
            var ends = item.Split('-');
            if (ends.Length > 2)
                return null;
            var from = Array.IndexOf(Months, ends[0]);
            var to = Array.IndexOf(Months, ends[^1]);
            if (from < 0 || to < 0)
                return null;
            for (var m = from; ; m = (m + 1) % 12)
            {
                result[m] = true;
                if (m == to)
                    break;
            }
        }
        return result;
    }

    private static List<WeekdayItem>? ParseWeekdays(string token)
    {
        var result = new List<WeekdayItem>();
        foreach (var item in WeekdayItems().Split(token).Where(i => i.Length > 0))
        {
            if (item is "PH" or "SH")
                continue;

            var match = WeekdayItemPattern().Match(item);
            if (!match.Success)
                return null;
            var from = Array.IndexOf(Weekdays, match.Groups[1].Value);
            var to = match.Groups[2].Success ? Array.IndexOf(Weekdays, match.Groups[2].Value) : from;
            if (from < 0 || to < 0)
                return null;

            var days = new bool[7];
            for (var d = from; ; d = (d + 1) % 7)
            {
                days[d] = true;
                if (d == to)
                    break;
            }

            int[]? nth = null;
            if (match.Groups[3].Success)
            {
                nth = match.Groups[3].Value.Split(',').Select(int.Parse).ToArray();
                if (nth.Any(n => n is 0 or > 5 or < -1))
                    return null;
            }
            result.Add(new WeekdayItem(days, nth));
        }
        return result;
    }

    // Dni: przecinki poza nawiasami [] rozdzielają pozycje (Mo,Su[1,3]).
    [GeneratedRegex(@",(?![^\[]*\])")]
    private static partial Regex WeekdayItems();

    [GeneratedRegex(@"^([A-Z][a-z])(?:-([A-Z][a-z]))?(?:\[(-?\d(?:,-?\d)*)\])?$")]
    private static partial Regex WeekdayItemPattern();

    [GeneratedRegex(@"^(\d{1,2}):(\d{2})-(\d{1,2}):(\d{2})$")]
    private static partial Regex TimeRange();

    [GeneratedRegex(@"\s*,\s*")]
    private static partial Regex CommaSpaces();

    // Months/Weekdays = null – każdy miesiąc / dzień tygodnia.
    private sealed record Rule(bool[]? Months, List<WeekdayItem>? Weekdays, IReadOnlyList<OpenInterval> Intervals)
    {
        public bool Matches(DateOnly date)
            => (Months is null || Months[date.Month - 1])
            && (Weekdays is null || Weekdays.Any(w => w.Matches(date)));
    }

    // Nth – które wystąpienie dnia w miesiącu (1–5, −1 = ostatnie); null – każde.
    private sealed record WeekdayItem(bool[] Days, int[]? Nth)
    {
        public bool Matches(DateOnly date)
        {
            if (!Days[(int)date.DayOfWeek])
                return false;
            if (Nth is null)
                return true;
            var occurrence = (date.Day - 1) / 7 + 1;
            var isLast = date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month);
            return Nth.Any(n => n == occurrence || n == -1 && isLast);
        }
    }
}
