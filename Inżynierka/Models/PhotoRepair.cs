using System.ComponentModel.DataAnnotations;

namespace Inżynierka.Models;

// --- Propozycje zdjęć dla jednej atrakcji (POST /api/dev/attractions/{id}/photo-candidates) ---

public sealed record PhotoCandidatesResponse(
    Guid AttractionId,
    string Name,
    // place / exhibit – jakiego rodzaju propozycji szukano.
    string Kind,
    // Kategoria Commons obiektu (z Wikidata), jeśli jest.
    string? CommonsCategory,
    // Wyjaśnienie, gdy nic nie dodano (np. brak kategorii); null = brak uwag.
    string? Note,
    // Nowe propozycje zapisane jako pending_review – obejrzyj przez ImageUrl.
    IReadOnlyList<PhotoCandidate> Added,
    // Znalezione pliki odrzucone przez filtr licencji/formatu.
    IReadOnlyList<PhotoCandidateSkip> Skipped,
    // Pliki, które już są w bazie dla tej atrakcji (także odrzucone) – pominięte.
    int AlreadyKnown);

public sealed record PhotoCandidate(Guid PhotoId, string ImageUrl, string SourceFile, string Author,
    string License, string SourcePageUrl, string MatchMethod, string Kind, string? Caption);

// Pominięty plik – tylko do podglądu (miniatura prosto z Wikimedia), nic nie jest zapisywane.
public sealed record PhotoCandidateSkip(string SourceFile, string Reason, string ThumbnailUrl,
    string SourcePageUrl, string License, string Author);

// --- Kolejka przeglądu (GET /api/dev/photos/queue) ---

public sealed record PhotoReviewQueue(
    // Propozycje pending_review, pogrupowane kolejnością miejsc.
    IReadOnlyList<PendingPhoto> Pending,
    // Wszystkie atrakcje do przeglądu (aktywne i murale czekające na zdjęcie), z liczbą zdjęć.
    IReadOnlyList<PlaceForReview> Places);

public sealed record PendingPhoto(
    Guid PhotoId, Guid PlaceId, string PlaceName, string? City, string PlaceStatus,
    double Latitude, double Longitude, string ImageUrl, bool FileExists,
    string Author, string License, string LicenseUrl, string SourcePageUrl, string SourceFile,
    string MatchMethod, int ApprovedCount, string Kind, string? Caption, bool IsMuseum);

// ApprovedPhotos = zatwierdzone zdjęcia miejsca, ApprovedExhibits = zatwierdzone eksponaty, PendingCount = propozycje.
public sealed record PlaceForReview(Guid PlaceId, string Name, string? City, string Status, bool IsMuseum,
    int ApprovedPhotos, int ApprovedExhibits, int PendingCount);

// --- Decyzja o zdjęciu (POST /api/dev/photos/{photoId}/review) ---

public sealed class PhotoReviewRequest : IValidatableObject
{
    // approved = widoczne w API, rejected = odrzucone (nie wróci jako propozycja), pending_review = cofnięcie decyzji.
    [RegularExpression("^(approved|rejected|pending_review)$",
        ErrorMessage = "Dozwolone: approved, rejected, pending_review.")]
    public string? Status { get; set; }

    // Zmiana rodzaju: place (zdjęcie miejsca) albo exhibit (eksponat/wnętrze). Pomiń, żeby nie zmieniać.
    [RegularExpression("^(place|exhibit)$", ErrorMessage = "Dozwolone: place, exhibit.")]
    public string? Kind { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is null && Kind is null)
            yield return new ValidationResult("Podaj status, rodzaj albo oba.", [nameof(Status), nameof(Kind)]);
    }
}

public sealed record PhotoReviewResponse(Guid PhotoId, Guid PlaceId, string PlaceName, string Status, string Kind, int SortOrder);

// --- Wszystkie zdjęcia jednej atrakcji (GET /api/dev/attractions/{id}/photos) ---

public sealed record AttractionPhotoReview(
    Guid PlaceId, string Name, string? City, string Status, bool IsMuseum,
    double Latitude, double Longitude, string? CommonsHint,
    // Wszystkie zdjęcia (zatwierdzone, propozycje, odrzucone), w kolejności galerii.
    IReadOnlyList<ReviewPhoto> Photos);

// IsPrimary = pierwsze zatwierdzone zdjęcie swojego rodzaju (główne zdjęcie atrakcji albo pierwszy eksponat).
public sealed record ReviewPhoto(
    Guid PhotoId, string ImageUrl, bool FileExists, string Status, string Kind, string? Caption,
    int SortOrder, bool IsPrimary, string Provider, string Author, string License, string LicenseUrl,
    string SourcePageUrl, string SourceFile, string MatchMethod);

// --- Naprawa brakujących plików (POST /api/dev/photos/repair) ---

public sealed record PhotoRepairResponse(
    bool DryRun,
    // Wpisy zdjęć w bazie (bez odrzuconych).
    int Checked,
    // Plik jest na dysku.
    int Present,
    // Brak pliku przed naprawą.
    int Missing,
    int Downloaded,
    int Failed,
    // Brakujące, których nie pobrano w tym wywołaniu (limit lub dryRun).
    int Remaining,
    IReadOnlyList<PhotoRepairItem> Items);

// Outcome: missing (dryRun / poza limitem), downloaded, failed, manual_required (zdjęcie własne – nie da się pobrać), invalid_path.
public sealed record PhotoRepairItem(Guid PhotoId, Guid PlaceId, string PlaceName, string Status, string Outcome, string? Detail);
