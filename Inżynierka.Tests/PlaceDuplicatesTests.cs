using Inżynierka.Services;

namespace Inżynierka.Tests;

public class PlaceDuplicatesTests
{
    // Okolice Rynku w Rzeszowie; 0,001° szerokości ≈ 111 m.
    private const double Lat = 50.037, Lon = 22.004;

    private static ExistingPlace Place(string name, double dLat = 0, params string[] osm)
        => new(Guid.NewGuid(), name, "active", Lat + dLat, Lon, osm);

    [Fact]
    public void SameOsmObject_IsSameSource_EvenFarAwayAndDifferentName()
    {
        var matches = PlaceDuplicates.Find("Zupełnie inna nazwa", Lat + 0.05, Lon, "node/42", [Place("Grawitacja", 0, "node/42")]);

        Assert.Equal(PlaceDuplicates.SameSource, Assert.Single(matches).Level);
    }

    [Fact]
    public void SameOvertureObject_IsSameSource()
    {
        var key = PlaceDuplicates.OvertureKey("ef395dc1-b9c6-4fbe-b5bc-7c0626d93292");

        var match = Assert.Single(PlaceDuplicates.Find("Laser Arena", Lat, Lon, key, [Place("Laser Arena Rzeszów", 0, key)]));

        Assert.Equal(PlaceDuplicates.SameSource, match.Level);
        Assert.Contains("Overture", match.Reason);
    }

    [Fact]
    public void SimilarNameNearby_IsLikely()
    {
        var matches = PlaceDuplicates.Find("Park Trampolin Grawitacja", Lat, Lon, null,
            [Place("Grawitacja Rzeszów Park Trampolin", 0.0005)]);

        Assert.Equal(PlaceDuplicates.Likely, Assert.Single(matches).Level);
    }

    [Fact]
    public void SameNameUpTo1Km_IsLikely()
    {
        // Ręcznie wpisany adres może dać punkt kilkaset metrów dalej niż w OSM.
        var matches = PlaceDuplicates.Find("Exitmania", Lat, Lon, null, [Place("Exitmania Escape Room", 0.006)]);

        Assert.Equal(PlaceDuplicates.Likely, Assert.Single(matches).Level);
    }

    [Fact]
    public void DifferentNameInSameBuilding_IsOnlyNearby()
    {
        var matches = PlaceDuplicates.Find("Laser Tag Arena", Lat, Lon, null, [Place("Aloha Bowling & Club", 0.0001)]);

        Assert.Equal(PlaceDuplicates.Nearby, Assert.Single(matches).Level);
    }

    [Fact]
    public void DifferentNameFarAway_IsNoMatch()
    {
        Assert.Empty(PlaceDuplicates.Find("Laser Tag Arena", Lat, Lon, null, [Place("Aloha Bowling & Club", 0.01)]));
    }

    [Fact]
    public void MatchesAreOrderedBySeverity()
    {
        var matches = PlaceDuplicates.Find("Grawitacja", Lat, Lon, "way/7",
            [Place("Kawiarnia", 0.0001), Place("Grawitacja Park Trampolin", 0.0005), Place("Coś", 0.02, "way/7")]);

        Assert.Equal(new[] { PlaceDuplicates.SameSource, PlaceDuplicates.Likely, PlaceDuplicates.Nearby },
            matches.Select(m => m.Level).ToArray());
    }

    [Theory]
    [InlineData("Grawitacja Rzeszów Park Trampolin", "Park Trampolin GRAWITACJA", 1.0)]
    [InlineData("Kręgielnia Galaktyka", "Galaktyka - kręgielnia i restauracja", 0.75)]
    [InlineData("Exitmania", "WydostanSie", 0.0)]
    public void NameSimilarity_IgnoresCaseDiacriticsAndGenericWords(string a, string b, double atLeast)
    {
        var similarity = PlaceDuplicates.NameSimilarity(a, b);

        if (atLeast == 0) Assert.Equal(0, similarity);
        else Assert.True(similarity >= atLeast, $"podobieństwo {similarity}");
    }

    [Fact]
    public void SignificantTokens_FallBackToAllWordsWhenOnlyGeneric()
    {
        Assert.True(PlaceDuplicates.SignificantTokens("Laser Tag Arena").SetEquals(new[] { "laser", "tag", "arena" }));
        Assert.True(PlaceDuplicates.SignificantTokens("Łódź Escape Room").SetEquals(new[] { "lodz" }));
    }

    [Fact]
    public void DistanceMeters_RoughlyMatchesDegrees()
    {
        var distance = PlaceDuplicates.DistanceMeters(Lat, Lon, Lat + 0.001, Lon);

        Assert.InRange(distance, 110, 112);
    }

    [Theory]
    [InlineData("""{"sourceDetails":{"raw":{"osm_type":"n","osm_id":5245675351}}}""", "node/5245675351")]
    [InlineData("""{"sourceDetails":{"raw":{"osm_type":"w","osm_id":"12"}}}""", "way/12")]
    [InlineData("""{"sourceDetails":{"raw":{"osm_type":"relation","osm_id":3}}}""", "relation/3")]
    [InlineData("""{"sourceDetails":{"raw":{"osm_type":"x","osm_id":3}}}""", null)]
    [InlineData("""{"inne":1}""", null)]
    [InlineData("nie json", null)]
    public void OsmKeyFromRawData(string raw, string? expected)
    {
        Assert.Equal(expected, PlaceDuplicates.OsmKeyFromRawData(raw));
    }
}
