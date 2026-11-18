using System.Net.Http.Json;
using System.Text.Json;
using Inżynierka.Planning;

namespace Inżynierka.Services;

// Macierz czasów i odległości OpenRouteService (HeiGIT, plan Standard: Matrix 500 zapytań/dzień, 40/min,
// do 3500 par w zapytaniu). Używana tylko przez narzędzie Development – planer czyta wyniki z bazy.
// Wysyłamy wyłącznie współrzędne atrakcji z katalogu (regulamin HeiGIT zabrania przesyłania danych osobowych).
public sealed class OpenRouteServiceMatrixService(HttpClient client, IConfiguration configuration)
{
    public const string Attribution = "© openrouteservice by HeiGIT | Data from OpenStreetMap";
    public const string License = "CC-BY-SA 4.0";
    public const int MaxElementsPerRequest = 3500;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["OpenRouteService:ApiKey"]);

    // Durations[s][d] w sekundach, Distances[s][d] w metrach; null = brak trasy między punktami.
    public async Task<MatrixResult> GetMatrixAsync(string profile, IReadOnlyList<GeoPoint> sources,
        IReadOnlyList<GeoPoint> destinations, CancellationToken cancellationToken)
    {
        if (sources.Count == 0 || destinations.Count == 0)
            throw new ArgumentException("Macierz wymaga co najmniej jednego źródła i celu.");
        if (sources.Count * destinations.Count > MaxElementsPerRequest)
            throw new ArgumentException($"Najwyżej {MaxElementsPerRequest} par w jednym zapytaniu.");

        // ORS przyjmuje [długość, szerokość]; źródła i cele jako indeksy we wspólnej liście punktów.
        var locations = sources.Concat(destinations).Select(p => new[] { p.Longitude, p.Latitude }).ToArray();
        var body = new
        {
            locations,
            sources = Enumerable.Range(0, sources.Count).ToArray(),
            destinations = Enumerable.Range(sources.Count, destinations.Count).ToArray(),
            metrics = new[] { "duration", "distance" },
            units = "m"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v2/matrix/{Uri.EscapeDataString(profile)}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Authorization", configuration["OpenRouteService:ApiKey"]);

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // 429 (limit), 401/403 (klucz), 400 (np. punkt daleko od drogi) – treść błędu ORS bez klucza i URL.
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"OpenRouteService {(int)response.StatusCode}: {Truncate(error, 300)}",
                null, response.StatusCode);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        return new MatrixResult(
            ReadTable(document.RootElement, "durations", sources.Count, destinations.Count),
            ReadTable(document.RootElement, "distances", sources.Count, destinations.Count));
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    private static double?[][] ReadTable(JsonElement root, string name, int rows, int columns)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var table)
            || table.ValueKind != JsonValueKind.Array || table.GetArrayLength() != rows)
            throw new JsonException($"Brak tabeli {name} o oczekiwanym rozmiarze.");

        var result = new double?[rows][];
        var r = 0;
        foreach (var row in table.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != columns)
                throw new JsonException($"Nieprawidłowy wiersz tabeli {name}.");
            result[r] = new double?[columns];
            var c = 0;
            foreach (var cell in row.EnumerateArray())
            {
                result[r][c++] = cell.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.Number when cell.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 => value,
                    _ => throw new JsonException($"Nieprawidłowa wartość w tabeli {name}.")
                };
            }
            r++;
        }
        return result;
    }
}

public sealed record MatrixResult(double?[][] Durations, double?[][] Distances);
