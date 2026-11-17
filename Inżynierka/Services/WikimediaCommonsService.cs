using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Inżynierka.Services;

// Plik z Commons z metadanymi potrzebnymi do podpisu zdjęcia (autor, licencja, źródło).
public sealed record CommonsImage(
    string Title,
    string DescriptionUrl,
    string OriginalUrl,
    string ThumbnailUrl,
    string Mime,
    string License,
    string LicenseUrl,
    string Author,
    string Credit,
    string Restrictions,
    bool Copyrighted,
    string MetadataJson,
    // Krótki opis pliku (ObjectName albo ImageDescription), np. nazwa eksponatu; "" = brak.
    string Caption = "");

// Zapytania do API Wikimedia Commons i Wikidata (bez klucza). Tylko odczyt.
public sealed partial class WikimediaCommonsService(HttpClient client)
{
    // Kategoria Commons obiektu z Wikidata (właściwość P373), np. Q773316 → "Łańcut Castle"; null = brak.
    public async Task<string?> GetCommonsCategoryAsync(string wikidataId, CancellationToken cancellationToken)
    {
        if (!WikidataIdPattern().IsMatch(wikidataId))
            return null;
        using var response = await client.GetAsync(
            $"https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids={wikidataId}", cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("entities", out var entities)
            && entities.TryGetProperty(wikidataId, out var entity)
            && entity.TryGetProperty("claims", out var claims)
            && claims.TryGetProperty("P373", out var p373) && p373.GetArrayLength() > 0
            && p373[0].TryGetProperty("mainsnak", out var snak)
            && snak.TryGetProperty("datavalue", out var value)
            && value.TryGetProperty("value", out var category) && category.ValueKind == JsonValueKind.String
                ? category.GetString()
                : null;
    }

    // Elementy kategorii: type "file" (pliki) albo "subcat" (podkategorie). Zwraca pełne tytuły (File:…, Category:…).
    public async Task<IReadOnlyList<string>> GetCategoryMembersAsync(string category, string type, int limit,
        CancellationToken cancellationToken)
    {
        var title = category.StartsWith("Category:", StringComparison.Ordinal) ? category : "Category:" + category;
        using var document = await QueryAsync(new()
        {
            ["list"] = "categorymembers", ["cmtitle"] = title, ["cmtype"] = type,
            ["cmlimit"] = limit.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return document.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("categorymembers", out var items)
            ? items.EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList()
            : [];
    }

    // Podkategorie z eksponatami lub wnętrzami, np. "Collections of Rzeszów Regional Museum", "Interior of the Łańcut Castle".
    public static bool IsExhibitCategory(string title) => ExhibitCategoryPattern().IsMatch(title);

    // Identyfikator Wikidata samego obiektu z surowych danych źródła (place_sources.raw_data, tag OSM "wikidata").
    // Celowo pomijamy "subject:wikidata" – wskazuje osobę lub temat pomnika (np. Adam Mickiewicz), nie obiekt.
    public static string? ObjectWikidataId(string rawDataJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawDataJson);
            if (document.RootElement.TryGetProperty("sourceDetails", out var details)
                && details.TryGetProperty("raw", out var raw)
                && raw.TryGetProperty("wikidata", out var wikidata) && wikidata.ValueKind == JsonValueKind.String)
            {
                var id = wikidata.GetString()!.Split(';')[0].Trim();
                return WikidataIdPattern().IsMatch(id) ? id : null;
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    // Wyszukiwanie plików po tekście (np. "Biały Ogród" Rzeszów).
    public async Task<IReadOnlyList<string>> SearchFilesAsync(string text, int limit, CancellationToken cancellationToken)
    {
        using var document = await QueryAsync(new()
        {
            ["list"] = "search", ["srsearch"] = text, ["srnamespace"] = "6",
            ["srlimit"] = limit.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return document.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("search", out var items)
            ? items.EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList()
            : [];
    }

    // Pliki z geotagiem w promieniu od punktu (namespace 6 = pliki).
    public async Task<IReadOnlyList<string>> GeoSearchFilesAsync(double latitude, double longitude, int radiusMeters,
        int limit, CancellationToken cancellationToken)
    {
        using var document = await QueryAsync(new()
        {
            ["list"] = "geosearch",
            ["gscoord"] = string.Create(CultureInfo.InvariantCulture, $"{latitude}|{longitude}"),
            ["gsradius"] = radiusMeters.ToString(CultureInfo.InvariantCulture),
            ["gsnamespace"] = "6", ["gslimit"] = limit.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);
        return document.RootElement.TryGetProperty("query", out var query) && query.TryGetProperty("geosearch", out var items)
            ? items.EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToList()
            : [];
    }

    // Metadane plików (do 50 tytułów naraz); miniatura o szerokości 1280 px jak w skrypcie Python.
    public async Task<IReadOnlyList<CommonsImage>> GetImagesAsync(IReadOnlyList<string> titles, CancellationToken cancellationToken)
    {
        var result = new List<CommonsImage>();
        foreach (var batch in titles.Chunk(50))
        {
            using var document = await QueryAsync(new()
            {
                ["titles"] = string.Join('|', batch), ["prop"] = "imageinfo",
                ["iiprop"] = "url|extmetadata|mime", ["iiurlwidth"] = "1280"
            }, cancellationToken);
            if (!document.RootElement.TryGetProperty("query", out var query) || !query.TryGetProperty("pages", out var pages))
                continue;
            foreach (var page in pages.EnumerateObject().Select(p => p.Value))
            {
                if (!page.TryGetProperty("imageinfo", out var infos) || infos.GetArrayLength() == 0)
                    continue;
                var info = infos[0];
                var meta = info.TryGetProperty("extmetadata", out var m) ? m : default;
                string Meta(string key) => meta.ValueKind == JsonValueKind.Object && meta.TryGetProperty(key, out var v)
                    && v.TryGetProperty("value", out var value) ? PlainText(value.ToString()) : "";
                string Prop(string key) => info.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";

                result.Add(new CommonsImage(
                    page.GetProperty("title").GetString()!,
                    Prop("descriptionurl"), Prop("url"),
                    info.TryGetProperty("thumburl", out var thumb) ? thumb.GetString()! : Prop("url"),
                    Prop("mime"), Meta("LicenseShortName"), Meta("LicenseUrl"), Meta("Artist"), Meta("Credit"),
                    Meta("Restrictions"), Meta("Copyrighted") == "True",
                    meta.ValueKind == JsonValueKind.Object ? meta.GetRawText() : "{}",
                    ShortCaption(Meta("ObjectName"), Meta("ImageDescription"))));
            }
        }
        return result;
    }

    // Zasady jak w scripts/fetch_place_photos.py: CC BY / CC BY-SA 1.0, 2.0, 2.5, 3.0, 4.0 (także krajowe, np. „3.0 pl”),
    // CC0 lub domena publiczna; wymagany autor i link licencji; bez dodatkowych ograniczeń; JPEG/PNG/WebP.
    public static string? RejectionReason(CommonsImage image)
    {
        if (!LicensePattern().IsMatch(image.License)) return $"Licencja „{image.License}” poza listą dozwolonych.";
        if (string.IsNullOrWhiteSpace(image.LicenseUrl)) return "Brak linku do licencji.";
        if (!string.IsNullOrWhiteSpace(image.Restrictions)) return "Plik ma dodatkowe ograniczenia.";
        if (image.Copyrighted && image.License == "Public domain") return "Sprzeczne oznaczenie domeny publicznej.";
        if (image.Mime is not ("image/jpeg" or "image/png" or "image/webp")) return $"Format {image.Mime} nieobsługiwany.";
        if (string.IsNullOrWhiteSpace(image.Author)) return "Brak autora.";
        return null;
    }

    // Podpis: nazwa obiektu, a gdy jej brak – początek opisu; maks. 200 znaków.
    public static string ShortCaption(string objectName, string description)
    {
        var text = string.IsNullOrWhiteSpace(objectName) ? description : objectName;
        text = text.Trim();
        return text.Length <= 200 ? text : text[..197].TrimEnd() + "…";
    }

    // Metadane Commons zawierają HTML (np. link do strony autora) – zostawiamy sam tekst.
    public static string PlainText(string html)
        => WhitespacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(html, " ")), " ").Trim();

    private async Task<JsonDocument> QueryAsync(Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        parameters["action"] = "query";
        parameters["format"] = "json";
        using var content = new FormUrlEncodedContent(parameters);
        var query = await content.ReadAsStringAsync(cancellationToken);
        using var response = await client.GetAsync("w/api.php?" + query, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    [GeneratedRegex(@"^(CC BY(-SA)? (1\.0|2\.0|2\.5|3\.0|4\.0)( [a-z]{2,3})?|CC0|Public domain)$")]
    private static partial Regex LicensePattern();

    [GeneratedRegex(@"^Q[1-9]\d*$")]
    private static partial Regex WikidataIdPattern();

    [GeneratedRegex(@"\b(collections?|interiors?|exhibits?|exhibitions?|artworks?|paintings|objects|wnętrz\w*|zbior\w*|eksponat\w*|kolekcj\w*)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ExhibitCategoryPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
