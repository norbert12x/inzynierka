using Inżynierka.Planning;

namespace Inżynierka.Services;

public sealed record MatrixPlace(Guid Id, GeoPoint Location);

// Jedno zapytanie do macierzy: Sources × Destinations ≤ limit par. Pairs – pary do zapisania
// (indeksy w Sources i Destinations); pozostałe komórki wyniku są ignorowane (znane albo za daleko).
public sealed record MatrixBatch(
    IReadOnlyList<MatrixPlace> Sources,
    IReadOnlyList<MatrixPlace> Destinations,
    IReadOnlyList<(int Source, int Destination)> Pairs);

// Dzieli brakujące pary atrakcji na jak najmniej zapytań do macierzy (bez HTTP i bazy – testy xUnit).
public static class TravelMatrixBatches
{
    // Komórka siatki do porządkowania punktów: ok. 5,5 km × 4,3 km na naszej szerokości.
    private const double GridLatitudeDegrees = 0.05;
    private const double GridLongitudeDegrees = 0.06;

    // isKnown(from, to) – para jest już w bazie (także jako „brak trasy”); pary dalsze niż maxDistanceMeters
    // w linii prostej pomijamy (planer użyje szacunku).
    public static IReadOnlyList<MatrixBatch> Plan(IReadOnlyList<MatrixPlace> places, Func<Guid, Guid, bool> isKnown,
        double maxDistanceMeters, int maxElementsPerRequest = OpenRouteServiceMatrixService.MaxElementsPerRequest)
    {
        if (maxElementsPerRequest < 1)
            throw new ArgumentOutOfRangeException(nameof(maxElementsPerRequest));

        // Bloki z punktów leżących blisko siebie (wiersze siatki, w wierszu wężykiem): przy małym progu
        // (pieszo) większość par bloków jest wtedy w całości za daleko i nie wymaga zapytania.
        var ordered = places
            .Select(p => new
            {
                Place = p,
                Row = (int)Math.Floor(p.Location.Latitude / GridLatitudeDegrees),
                Column = (int)Math.Floor(p.Location.Longitude / GridLongitudeDegrees)
            })
            .OrderBy(p => p.Row)
            .ThenBy(p => p.Row % 2 == 0 ? p.Column : -p.Column)
            .ThenBy(p => p.Place.Id)
            .Select(p => p.Place)
            .ToList();

        var blockSize = Math.Max(1, (int)Math.Floor(Math.Sqrt(maxElementsPerRequest)));
        var blocks = ordered.Chunk(blockSize).ToList();
        var batches = new List<MatrixBatch>();

        foreach (var sourceBlock in blocks)
        foreach (var destinationBlock in blocks)
        {
            var needed = new List<(MatrixPlace From, MatrixPlace To)>();
            foreach (var from in sourceBlock)
            foreach (var to in destinationBlock)
            {
                if (from.Id == to.Id || isKnown(from.Id, to.Id))
                    continue;
                if (TravelTimeEstimator.StraightLineMeters(from.Location, to.Location) > maxDistanceMeters)
                    continue;
                needed.Add((from, to));
            }
            if (needed.Count == 0)
                continue;

            // Tylko wiersze i kolumny, w których coś brakuje – mniejsze zapytanie, ten sam koszt w limicie.
            var sources = needed.Select(p => p.From).Distinct().ToList();
            var destinations = needed.Select(p => p.To).Distinct().ToList();
            var sourceIndex = sources.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var destinationIndex = destinations.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
            var pairs = needed.Select(p => (sourceIndex[p.From.Id], destinationIndex[p.To.Id])).ToList();
            batches.Add(new MatrixBatch(sources, destinations, pairs));
        }

        return batches;
    }
}
