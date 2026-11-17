namespace Inżynierka.Models;

// Kind: "place" (zdjęcie atrakcji) albo "exhibit" (eksponat/wnętrze muzeum); Caption: opis z Commons lub null.
public sealed record AttractionPhoto(
    Guid Id, string ImageUrl, string Author, string Credit, string License,
    string LicenseUrl, string SourcePageUrl, string Status, string Kind, string? Caption);

public sealed record AttractionPhotosResponse(Guid AttractionId, IReadOnlyList<AttractionPhoto> Items);
