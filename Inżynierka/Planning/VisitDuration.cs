using System.Text.Json.Serialization;

namespace Inżynierka.Planning;

// W JSON jako "place" / "category_default" / "user".
[JsonConverter(typeof(JsonStringEnumConverter<VisitMinutesSource>))]
public enum VisitMinutesSource
{
    // Czas zapisany przy atrakcji (np. ręczna korekta).
    [JsonStringEnumMemberName("place")]
    Place,
    // Szacunek: najdłuższy domyślny czas spośród kategorii atrakcji.
    [JsonStringEnumMemberName("category_default")]
    CategoryDefault,
    // Czas podany przez użytkownika w planie.
    [JsonStringEnumMemberName("user")]
    User
}

public sealed record VisitDuration(int Minutes, VisitMinutesSource Source)
{
    // Czas wizyty atrakcji: własny czas atrakcji ma pierwszeństwo, inaczej najdłuższy czas jej kategorii
    // (muzeum w zamku = czas muzeum). Bez kategorii – fallbackMinutes (czas kategorii „other”).
    public static VisitDuration Resolve(int? placeMinutes, IEnumerable<int> categoryMinutes, int fallbackMinutes)
    {
        if (placeMinutes is > 0)
            return new VisitDuration(placeMinutes.Value, VisitMinutesSource.Place);

        var longest = categoryMinutes.Where(m => m > 0).DefaultIfEmpty(fallbackMinutes).Max();
        return new VisitDuration(longest, VisitMinutesSource.CategoryDefault);
    }
}
