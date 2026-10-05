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
    public string Status { get; set; } = "active";
    public string Kind { get; set; } = "attraction";
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
    public string Status { get; set; } = "pending_review";
}

public sealed class CatalogCategory
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
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
