namespace Inżynierka.Planning;

// Kolejność atrakcji w dniu od punktu startu do punktu końca (różne albo te same) – minimalny łączny czas
// przejazdów. Najbliższy sąsiad daje trasę startową, 2-opt odwraca jej fragmenty, dopóki to skraca trasę.
// Liczymy pełny koszt trasy po każdym odwróceniu, bo czasy mogą być różne w obie strony (ORS, parkowanie);
// przy kilkunastu atrakcjach dziennie to ułamek sekundy.
public static class RouteOptimizer
{
    public static IReadOnlyList<PlanAttraction> Order(
        PlanPoint start, PlanPoint end, IReadOnlyList<PlanAttraction> attractions, ITravelTimes travel)
    {
        var route = NearestNeighbour(start, attractions, travel);
        ImproveTwoOpt(start, end, route, travel);
        return route;
    }

    // Łączny czas przejazdów: start → atrakcje → koniec (z parkowaniem, bez wizyt).
    public static int TravelMinutes(PlanPoint start, PlanPoint end, IReadOnlyList<PlanAttraction> route, ITravelTimes travel)
    {
        var total = 0;
        var current = start;
        foreach (var attraction in route)
        {
            total += travel.GetLeg(current, attraction.Point).Minutes;
            current = attraction.Point;
        }
        return total + travel.GetLeg(current, end).Minutes;
    }

    // Za każdym razem najbliższa (czasowo) nieodwiedzona atrakcja; remis – wcześniejsza na liście wejściowej.
    private static List<PlanAttraction> NearestNeighbour(PlanPoint start, IReadOnlyList<PlanAttraction> attractions, ITravelTimes travel)
    {
        var left = attractions.ToList();
        var route = new List<PlanAttraction>(left.Count);
        var current = start;
        while (left.Count > 0)
        {
            var next = left.MinBy(a => travel.GetLeg(current, a.Point).Minutes)!;
            left.Remove(next);
            route.Add(next);
            current = next.Point;
        }
        return route;
    }

    private static void ImproveTwoOpt(PlanPoint start, PlanPoint end, List<PlanAttraction> route, ITravelTimes travel)
    {
        var best = TravelMinutes(start, end, route, travel);
        var improved = true;
        while (improved)
        {
            improved = false;
            for (var i = 0; i < route.Count - 1; i++)
            {
                for (var j = i + 1; j < route.Count; j++)
                {
                    route.Reverse(i, j - i + 1);
                    var cost = TravelMinutes(start, end, route, travel);
                    if (cost < best)
                    {
                        best = cost;
                        improved = true;
                    }
                    else
                    {
                        route.Reverse(i, j - i + 1); // cofnięcie
                    }
                }
            }
        }
    }
}
