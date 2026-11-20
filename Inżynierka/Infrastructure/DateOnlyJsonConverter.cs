using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inżynierka.Infrastructure;

// Daty w API jako "yyyy-MM-dd" (jak domyślnie), ale z polskim komunikatem przy złej dacie (np. 2027-02-30).
public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new JsonException("Data w formacie RRRR-MM-DD, np. \"2027-05-15\".");
    }

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
