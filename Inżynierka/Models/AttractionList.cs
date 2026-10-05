using System.ComponentModel.DataAnnotations;

namespace Inżynierka.Models;

public sealed class AttractionListQuery : IValidatableObject
{
    [Range(1, 1_000_000)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;

    [StringLength(100)]
    public string? Search { get; set; }

    [StringLength(50)]
    public string? Category { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [Range(-90d, 90d)]
    public double? Latitude { get; set; }

    [Range(-180d, 180d)]
    public double? Longitude { get; set; }

    [Range(0.1d, 100d)]
    public double? RadiusKm { get; set; }

    public bool RequirePhoto { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasArea = Latitude.HasValue || Longitude.HasValue || RadiusKm.HasValue;
        if (hasArea && (!Latitude.HasValue || !Longitude.HasValue || !RadiusKm.HasValue))
            yield return new ValidationResult("Podaj razem latitude, longitude i radiusKm.",
                [nameof(Latitude), nameof(Longitude), nameof(RadiusKm)]);
        if (hasArea && !string.IsNullOrWhiteSpace(City))
            yield return new ValidationResult("Wybierz filtr miasta albo promień od punktu; nie łącz tych trybów.",
                [nameof(City), nameof(RadiusKm)]);
        if (new[] { Latitude, Longitude, RadiusKm }.Any(value => value.HasValue && !double.IsFinite(value.Value)))
            yield return new ValidationResult("Współrzędne i promień muszą być skończonymi liczbami.",
                [nameof(Latitude), nameof(Longitude), nameof(RadiusKm)]);
    }
}

public sealed record AttractionListResponse(
    IReadOnlyList<AttractionListItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public sealed record AttractionListItem(
    Guid Id,
    string Name,
    string? City,
    string? Address,
    double Latitude,
    double Longitude,
    IReadOnlyList<AttractionCategory> Categories,
    string? OpeningHours,
    string OpeningHoursVerification,
    DateOnly? OpeningHoursConfirmedOn,
    double? DistanceKm,
    bool HasPhoto,
    int PendingPhotoCount);
