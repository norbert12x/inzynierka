using NetTopologySuite.Geometries;

namespace Inżynierka.Data.Entities;

public sealed class CatalogPlace
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    // X = długość, Y = szerokość geograficzna; SRID = 4326.
    public Point Location { get; set; } = null!;
    public string Status { get; set; } = PlaceStatuses.Active;
    public string Kind { get; set; } = PlaceKinds.Attraction;
    public Guid? ParentId { get; set; }
    public CatalogPlace? Parent { get; set; }
    public ICollection<CatalogPlace> Components { get; set; } = new List<CatalogPlace>();
    public string? OpeningHours { get; set; }
    public string OpeningHoursVerification { get; set; } = "unverified";
    public DateOnly? OpeningHoursConfirmedOn { get; set; }
    public string? Website { get; set; }
    public int? EstimatedVisitMinutes { get; set; }
    public string[] ManuallyEditedFields { get; set; } = [];
    public ICollection<PlaceCategory> Categories { get; set; } = new List<PlaceCategory>();
    public ICollection<PlaceSource> Sources { get; set; } = new List<PlaceSource>();
    public ICollection<PlacePhoto> Photos { get; set; } = new List<PlacePhoto>();
}

public sealed class PlacePhoto
{
    public Guid Id { get; set; }
    public Guid PlaceId { get; set; }
    public CatalogPlace Place { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public string SourcePageUrl { get; set; } = "";
    public string OriginalUrl { get; set; } = "";
    public string ThumbnailUrl { get; set; } = "";
    public string CachedRelativePath { get; set; } = "";
    public string Author { get; set; } = "";
    public string Credit { get; set; } = "";
    public string License { get; set; } = "";
    public string LicenseUrl { get; set; } = "";
    public string MatchMethod { get; set; } = "";
    public string Metadata { get; set; } = "{}";
    public DateTimeOffset FetchedAtUtc { get; set; }
    public string Status { get; set; } = PhotoStatuses.PendingReview;
    // "place" = zdjęcie samej atrakcji, "exhibit" = eksponat lub wnętrze muzeum (osobna galeria).
    public string Kind { get; set; } = PhotoKinds.Place;
    // Krótki opis z Commons (np. nazwa eksponatu); null = brak.
    public string? Caption { get; set; }
    // Kolejność w galerii (rosnąco); pierwsze zatwierdzone zdjęcie rodzaju "place" = zdjęcie główne atrakcji.
    public int SortOrder { get; set; }
}

// Wartości tekstowe kolumn (ograniczenia CHECK w bazie) – używaj stałych zamiast napisów w kodzie.
public static class PlaceStatuses
{
    public const string Active = "active";
    // Np. mural bez zdjęcia – niewidoczny w katalogu do czasu uzupełnienia.
    public const string AwaitingPhoto = "awaiting_photo";
    public const string Removed = "removed";
    public const string Component = "component";
}

public static class PlaceKinds
{
    public const string Attraction = "attraction";
    // Element większego obiektu (np. tablica w parku), powiązany przez ParentId.
    public const string Component = "component";
}

public static class PhotoStatuses
{
    public const string PendingReview = "pending_review";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public static class PhotoKinds
{
    public const string Place = "place";
    public const string Exhibit = "exhibit";
}

public static class PhotoProviders
{
    public const string WikimediaCommons = "wikimedia_commons";
    // Zdjęcie własne albo udostępnione przez autora (import: scripts/archiwum/import_place_photo.py; wgrywanie na /dev/zdjecia – plan 1.9).
    public const string UserProvided = "user_provided";
}

public static class SourceProviders
{
    // Import 600 miejsc z Geoapify (dane OpenStreetMap), październik 2026.
    public const string Geoapify = "geoapify";
    public const string Overture = "overture";
    public const string Manual = "manual";
}

public static class CatalogQueries
{
    // Atrakcje widoczne w publicznym katalogu.
    public static IQueryable<CatalogPlace> PublicAttractions(this IQueryable<CatalogPlace> places)
        => places.Where(p => p.Kind == PlaceKinds.Attraction && p.Status == PlaceStatuses.Active);

    // Atrakcje do pracy redakcyjnej: aktywne i czekające na zdjęcie.
    public static IQueryable<CatalogPlace> ReviewableAttractions(this IQueryable<CatalogPlace> places)
        => places.Where(p => p.Kind == PlaceKinds.Attraction
            && (p.Status == PlaceStatuses.Active || p.Status == PlaceStatuses.AwaitingPhoto));
}

public sealed class CatalogCategory
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    // Szacowany czas wizyty dla atrakcji bez własnego czasu (kolumna z database/005, migracja InitialCatalog).
    public int DefaultVisitMinutes { get; set; }
    public ICollection<PlaceCategory> Places { get; set; } = new List<PlaceCategory>();
}

public sealed class PlaceCategory
{
    public Guid PlaceId { get; set; }
    public CatalogPlace Place { get; set; } = null!;
    public string CategoryCode { get; set; } = "";
    public CatalogCategory Category { get; set; } = null!;
}

public sealed class PlaceSource
{
    public Guid Id { get; set; }
    public Guid PlaceId { get; set; }
    public CatalogPlace Place { get; set; } = null!;
    public string Provider { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public DateTimeOffset FetchedAtUtc { get; set; }
    // JSON dostawcy, oddzielony od edytowalnych danych katalogu.
    public string RawData { get; set; } = "{}";
}
