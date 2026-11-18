using System.ComponentModel.DataAnnotations;
using Inżynierka.Services;

namespace Inżynierka.Models;

// --- Kandydaci z Overture (GET /api/dev/places/overture-candidates) ---

public sealed record OvertureCandidatesResponse(string Release, DateTimeOffset FetchedAtUtc, string LicenseInfo, int Total,
    IReadOnlyList<OvertureCandidateItem> Items);

// Matches: możliwe duplikaty w katalogu (same_source = już jest, likely = prawdopodobnie, nearby = coś obok).
public sealed record OvertureCandidateItem(OvertureCandidate Candidate, IReadOnlyList<DuplicateMatch> Matches);

// --- Sprawdzenie duplikatów (POST /api/dev/places/duplicates) ---

public sealed class DuplicateCheckRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Range(-90d, 90d)]
    public double Latitude { get; set; }

    [Range(-180d, 180d)]
    public double Longitude { get; set; }

    // Identyfikator Overture, gdy miejsce pochodzi z Overture.
    [RegularExpression(OvertureIdPattern.Value)]
    public string? OvertureId { get; set; }
}

// --- Dodanie atrakcji (POST /api/dev/places) ---

public sealed class CreatePlaceRequest
{
    [Required, StringLength(200, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(300)]
    public string? Address { get; set; }

    [Range(-90d, 90d)]
    public double Latitude { get; set; }

    [Range(-180d, 180d)]
    public double Longitude { get; set; }

    // Kody z GET /api/attraction-categories, np. ["active-entertainment"].
    [Required, MinLength(1), MaxLength(5)]
    public List<string> CategoryCodes { get; set; } = [];

    [Url, StringLength(500)]
    public string? Website { get; set; }

    // Zapis jak w OSM, np. "Mo-Su 10:00-22:00"; nieweryfikowany.
    [StringLength(300)]
    public string? OpeningHours { get; set; }

    // Własny czas wizyty; pusty = domyślny czas kategorii.
    [Range(5, 600)]
    public int? EstimatedVisitMinutes { get; set; }

    // Gdy miejsce pochodzi z Overture: identyfikator z data/overture/…/candidates.json. Serwer sam odczytuje
    // dane źródłowe i oznaczenie licencji z wyciągu (nie ufa przesłanym danym). Pusty = dodanie ręczne.
    [RegularExpression(OvertureIdPattern.Value)]
    public string? OvertureId { get; set; }

    // true = dodaj mimo prawdopodobnego duplikatu (decyzja użytkownika). Ten sam obiekt w źródle nigdy nie przejdzie.
    public bool ConfirmDuplicate { get; set; }
}

public sealed record CreatePlaceResponse(Guid Id, string Name, string Source, IReadOnlyList<DuplicateMatch> Warnings);

// 409: możliwe duplikaty, które trzeba potwierdzić (albo blokada przy tym samym obiekcie w źródle).
public sealed record DuplicateConflict(string Title, bool CanConfirm, IReadOnlyList<DuplicateMatch> Matches);

internal static class OvertureIdPattern
{
    public const string Value = "^[0-9a-fA-F-]{16,64}$";
}
