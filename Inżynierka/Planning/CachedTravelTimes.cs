namespace Inżynierka.Planning;

// Każdy odcinek liczony raz – optymalizacja i przydział do dni pytają o te same pary wiele razy.
internal sealed class CachedTravelTimes(ITravelTimes inner) : ITravelTimes
{
    private readonly Dictionary<(PlanPoint, PlanPoint), TravelLeg> _legs = [];

    public static ITravelTimes Wrap(ITravelTimes travel) => travel as CachedTravelTimes ?? new CachedTravelTimes(travel);

    public TravelLeg GetLeg(PlanPoint from, PlanPoint to)
    {
        if (!_legs.TryGetValue((from, to), out var leg))
            _legs[(from, to)] = leg = inner.GetLeg(from, to);
        return leg;
    }
}
