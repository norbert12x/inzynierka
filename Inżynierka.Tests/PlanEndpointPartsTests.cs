using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Inżynierka.Configuration;
using Inżynierka.Infrastructure;
using Inżynierka.Models;
using Inżynierka.Planning;
using Microsoft.Extensions.Configuration;

namespace Inżynierka.Tests;

public class PlannerOptionsValidatorTests
{
    [Fact]
    public void Defaults_AreValid()
    {
        Assert.Empty(PlannerOptionsValidator.GetErrors(new PlannerOptions()));
    }

    [Fact]
    public void Binding_ReadsTimesAndCategories()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Planner:DefaultDayStart"] = "08:30",
            ["Planner:MealLatestStart"] = "15:00",
            ["Planner:EntranceCategories:0"] = "museums",
            ["Planner:EntranceCategories:1"] = "culture"
        }).Build();

        var options = configuration.GetSection(PlannerOptions.SectionName).Get<PlannerOptions>()!;

        Assert.Equal(new TimeOnly(8, 30), options.DefaultDayStart);
        Assert.Equal(new TimeOnly(15, 0), options.MealLatestStart);
        Assert.Equal(["culture", "museums"], options.EntranceCategoryCodes.Order());
    }

    [Fact]
    public void NoEntranceCategories_UsesDefaults()
    {
        Assert.Same(EntranceCategories.DefaultCodes, new PlannerOptions().EntranceCategoryCodes);
    }

    [Fact]
    public void InvalidValues_AreErrors()
    {
        var options = new PlannerOptions
        {
            DefaultDayStart = new TimeOnly(18, 0),
            DefaultDayEnd = new TimeOnly(9, 0),
            MealMinutes = 5,
            MealEarliestStart = new TimeOnly(15, 0),
            MealLatestStart = new TimeOnly(13, 0),
            OverrunToleranceMinutes = -1,
            MaxSuggestionsPerDay = 11,
            CandidateMaxDistanceKm = 0,
            SmallAttractionMaxMinutes = -1,
            OnTheWayMaxDetourMinutes = 31,
            EntranceCategories = [""]
        };

        // Godziny dnia, czas posiłku, okno posiłku, tolerancja, liczba propozycji, promień, mała atrakcja,
        // objazd „po drodze”, kategorie.
        Assert.Equal(9, PlannerOptionsValidator.GetErrors(options).Count);
    }
}

public class GoogleMapsLinksTests
{
    private static readonly GeoPoint Rynek = new(50.0375, 22.0049);
    private static readonly GeoPoint Zamek = new(50.0333, 22.0018);

    [Fact]
    public void Leg_UsesPlannerModeAndInvariantCoordinates()
    {
        Assert.Equal("https://www.google.com/maps/dir/?api=1&origin=50.0375%2C22.0049&destination=50.0333%2C22.0018&travelmode=walking",
            GoogleMapsLinks.Leg(Rynek, Zamek, TravelMode.Walking));
        Assert.EndsWith("&travelmode=driving", GoogleMapsLinks.Leg(Rynek, Zamek, TravelMode.Driving));
        Assert.EndsWith("&travelmode=transit", GoogleMapsLinks.Transit(Rynek, Zamek));
    }

    [Fact]
    public void Day_WithFewStops_OneLinkWithWaypoints()
    {
        var link = Assert.Single(GoogleMapsLinks.Day(Rynek, [Zamek, new GeoPoint(50.04, 22.01)], Rynek));

        Assert.Contains("&waypoints=50.0333%2C22.0018%7C50.04%2C22.01", link);
        Assert.Contains("origin=50.0375%2C22.0049&destination=50.0375%2C22.0049", link);
    }

    [Fact]
    public void Day_WithoutStops_DirectLinkWithoutWaypoints()
    {
        var link = Assert.Single(GoogleMapsLinks.Day(Rynek, [], Zamek));

        Assert.DoesNotContain("waypoints", link);
    }

    [Fact]
    public void Day_WithManyStops_SplitIntoChainedLinks()
    {
        // 12 wizyt + start + koniec = 14 punktów: link 1 – start, 9 pośrednich, wizyta 10; link 2 – od wizyty 10 do końca.
        var stops = Enumerable.Range(1, 12).Select(i => new GeoPoint(50 + i / 100.0, 22)).ToList();

        var links = GoogleMapsLinks.Day(Rynek, stops, Rynek);

        Assert.Equal(2, links.Count);
        Assert.Contains("destination=50.1%2C22", links[0]);
        Assert.Contains("origin=50.1%2C22", links[1]);
        Assert.All(links, l => Assert.True(l.Split("%7C").Length <= GoogleMapsLinks.MaxWaypoints));
    }
}

public class TimeOnlyJsonConverterTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new TimeOnlyJsonConverter(), new DateOnlyJsonConverter() }
    };

    [Fact]
    public void Date_ReadWriteIsoAndRejectInvalid()
    {
        Assert.Equal(new DateOnly(2027, 5, 15), JsonSerializer.Deserialize<DateOnly?>("\"2027-05-15\"", Json));
        Assert.Equal("\"2027-05-15\"", JsonSerializer.Serialize(new DateOnly(2027, 5, 15), Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateOnly>("\"2027-02-30\"", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateOnly>("\"15.05.2027\"", Json));
    }

    [Theory]
    [InlineData("\"09:30\"", 9, 30)]
    [InlineData("\"9:05\"", 9, 5)]
    [InlineData("\"18:00:00\"", 18, 0)]
    public void Read_AcceptsHourMinute(string json, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), JsonSerializer.Deserialize<TimeOnly>(json, Json));
    }

    [Theory]
    [InlineData("\"25:00\"")]
    [InlineData("\"rano\"")]
    [InlineData("930")]
    public void Read_InvalidTime_Throws(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TimeOnly>(json, Json));
    }

    [Fact]
    public void Write_HourMinute_AlsoForNullable()
    {
        Assert.Equal("\"09:05\"", JsonSerializer.Serialize(new TimeOnly(9, 5, 30), Json));
        Assert.Equal("\"14:00\"", JsonSerializer.Serialize<TimeOnly?>(new TimeOnly(14, 0), Json));
    }
}

public class PlanGenerateRequestTests
{
    private static PlanGenerateRequest Valid() => new()
    {
        Lodging = new PlanLocationRequest { Latitude = 50.0375, Longitude = 22.0049 },
        Days = [new PlanDayRequest { Date = new DateOnly(2027, 5, 15) }, new PlanDayRequest { Date = new DateOnly(2027, 5, 16) }],
        Attractions = [new PlanAttractionRequest { Id = Guid.NewGuid() }]
    };

    private static List<ValidationResult> Errors(PlanGenerateRequest request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void ValidRequest_NoErrors()
    {
        Assert.Empty(Errors(Valid()));
    }

    [Fact]
    public void EmptyAttractions_Error()
    {
        var request = Valid();
        request.Attractions = [];

        Assert.Contains(Errors(request), e => e.MemberNames.Contains(nameof(PlanGenerateRequest.Attractions)));
    }

    [Fact]
    public void MissingDaysOrLodging_Error()
    {
        var request = Valid();
        request.Days = null;
        request.Lodging = null;

        Assert.Equal(2, Errors(request).Count);
    }

    [Fact]
    public void DatesNotAscending_Error()
    {
        var request = Valid();
        request.Days!.Reverse();

        Assert.Contains(Errors(request), e => e.ErrorMessage!.Contains("rosnącej"));
    }

    [Fact]
    public void TooManyDays_Error()
    {
        var request = Valid();
        request.Days = Enumerable.Range(0, 15).Select(i => new PlanDayRequest { Date = new DateOnly(2027, 5, 1).AddDays(i) }).ToList();

        Assert.Contains(Errors(request), e => e.MemberNames.Contains(nameof(PlanGenerateRequest.Days)));
    }

    [Fact]
    public void DuplicateAttraction_Error()
    {
        var request = Valid();
        var id = request.Attractions![0].Id;
        request.Attractions.Add(new PlanAttractionRequest { Id = id, Mandatory = false });

        Assert.Contains(Errors(request), e => e.ErrorMessage!.Contains("więcej niż raz"));
    }
}
