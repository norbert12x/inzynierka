namespace Inżynierka.Models;

public sealed record AttractionPhoto(
    Guid Id, string ImageUrl, string Author, string Credit, string License,
    string LicenseUrl, string SourcePageUrl, string Status);

public sealed record AttractionPhotosResponse(Guid AttractionId, IReadOnlyList<AttractionPhoto> Items);
