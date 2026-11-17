using Inżynierka.Services;

namespace Inżynierka.Tests;

public class WikimediaCommonsTests
{
    private static CommonsImage Image(string license = "CC BY-SA 4.0", string licenseUrl = "https://creativecommons.org/licenses/by-sa/4.0",
        string author = "Jan Kowalski", string restrictions = "", bool copyrighted = true, string mime = "image/jpeg")
        => new("File:Test.jpg", "https://commons.wikimedia.org/wiki/File:Test.jpg", "https://upload.wikimedia.org/a.jpg",
            "https://upload.wikimedia.org/thumb/a.jpg", mime, license, licenseUrl, author, "Own work", restrictions, copyrighted, "{}");

    [Theory]
    [InlineData("CC BY-SA 4.0")]
    [InlineData("CC BY 3.0")]
    [InlineData("CC BY-SA 3.0 pl")]
    [InlineData("CC BY 2.0")]
    [InlineData("CC BY-SA 2.5")]
    [InlineData("CC BY 2.5 pl")]
    [InlineData("CC0")]
    public void AcceptedLicenses(string license)
    {
        Assert.Null(WikimediaCommonsService.RejectionReason(Image(license)));
    }

    [Theory]
    [InlineData("CC BY-NC 4.0")]
    [InlineData("CC BY-ND 4.0")]
    [InlineData("GFDL")]
    [InlineData("CC BY-SA 3.5")]
    [InlineData("")]
    public void RejectedLicenses(string license)
    {
        Assert.NotNull(WikimediaCommonsService.RejectionReason(Image(license)));
    }

    [Fact]
    public void MissingAuthor_IsRejected()
    {
        Assert.NotNull(WikimediaCommonsService.RejectionReason(Image(author: " ")));
    }

    [Fact]
    public void Restrictions_AreRejected()
    {
        Assert.NotNull(WikimediaCommonsService.RejectionReason(Image(restrictions: "trademarked")));
    }

    [Fact]
    public void UnsupportedFormat_IsRejected()
    {
        Assert.NotNull(WikimediaCommonsService.RejectionReason(Image(mime: "image/tiff")));
    }

    [Fact]
    public void PublicDomainMarkedCopyrighted_IsRejected()
    {
        Assert.NotNull(WikimediaCommonsService.RejectionReason(Image("Public domain", copyrighted: true)));
        Assert.Null(WikimediaCommonsService.RejectionReason(Image("Public domain", copyrighted: false)));
    }

    [Fact]
    public void PlainText_RemovesHtmlAndEntities()
    {
        var text = WikimediaCommonsService.PlainText("<a href=\"//commons.wikimedia.org/wiki/User:X\">Patryk&nbsp;D.</a>\n<span>(praca własna)</span>");

        Assert.Equal("Patryk D. (praca własna)", text);
    }

    [Theory]
    [InlineData("Category:Collections of Rzeszów Regional Museum", true)]
    [InlineData("Category:Interior of the Łańcut Castle", true)]
    [InlineData("Category:Barbarian Tsunami exhibition", true)]
    [InlineData("Category:Wnętrza zamku w Łańcucie", true)]
    [InlineData("Category:Exterior of the Łańcut Castle", false)]
    [InlineData("Category:Gates of the Łańcut Castle", false)]
    [InlineData("Category:19 3 Maja Street in Rzeszów", false)]
    public void IsExhibitCategory(string title, bool expected)
    {
        Assert.Equal(expected, WikimediaCommonsService.IsExhibitCategory(title));
    }

    [Fact]
    public void ObjectWikidataId_ReadsObjectTag()
    {
        const string raw = """{"sourceDetails":{"raw":{"name":"Muzeum - Zamek w Łańcucie","wikidata":"Q773316"}}}""";

        Assert.Equal("Q773316", WikimediaCommonsService.ObjectWikidataId(raw));
    }

    [Fact]
    public void ObjectWikidataId_IgnoresSubjectTag()
    {
        // Pomnik Mickiewicza: subject:wikidata wskazuje poetę, nie pomnik.
        const string raw = """{"sourceDetails":{"raw":{"name":"Pomnik Adama Mickiewicza","subject:wikidata":"Q79822"}}}""";

        Assert.Null(WikimediaCommonsService.ObjectWikidataId(raw));
    }

    [Theory]
    [InlineData("""{"sourceDetails":{"raw":{"wikidata":"Q1;Q2"}}}""", "Q1")]
    [InlineData("""{"sourceDetails":{"raw":{"wikidata":"nie-id"}}}""", null)]
    [InlineData("""{"inne":1}""", null)]
    [InlineData("to nie jest json", null)]
    public void ObjectWikidataId_EdgeCases(string raw, string? expected)
    {
        Assert.Equal(expected, WikimediaCommonsService.ObjectWikidataId(raw));
    }

    [Fact]
    public void ShortCaption_PrefersObjectNameAndTruncates()
    {
        Assert.Equal("Rogatywka", WikimediaCommonsService.ShortCaption("Rogatywka", "Długi opis"));
        Assert.Equal("Długi opis", WikimediaCommonsService.ShortCaption("", "Długi opis"));
        var caption = WikimediaCommonsService.ShortCaption("", new string('a', 300));
        Assert.Equal(198, caption.Length);
        Assert.EndsWith("…", caption);
    }
}
