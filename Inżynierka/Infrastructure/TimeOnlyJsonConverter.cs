using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inżynierka.Infrastructure;

// Godziny w API jako "HH:mm" (plan dnia nie potrzebuje sekund). Odczyt przyjmuje też "H:mm" i "HH:mm:ss".
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = ["H:mm", "HH:mm", "HH:mm:ss"];

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new JsonException("Godzina w formacie HH:mm, np. \"09:30\".");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
