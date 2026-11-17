namespace Inżynierka.Models;

// Szczegóły jednej aktywnej atrakcji. Pola null oznaczają brak danych, nie wartość domyślną:
// OpeningHours = null to „nieznane godziny”, nie „całodobowo”.
public sealed record AttractionDetails(
    Guid Id,
    string Name,
    string? Description,
    string? City,
    string? Address,
    double Latitude,
    double Longitude,
    string? Website,
    IReadOnlyList<AttractionCategory> Categories,
    string? OpeningHours,
    string OpeningHoursVerification,
    DateOnly? OpeningHoursConfirmedOn,
    // Czas wizyty zapisany przy atrakcji; domyślne czasy według kategorii dojdą w zadaniu 1.2.
    int? EstimatedVisitMinutes,
    // Elementy obiektu (np. tablice, rzeźby w parku) przypisane do tej atrakcji.
    IReadOnlyList<AttractionComponent> Components,
    // Tylko zatwierdzone zdjęcia, z autorem i licencją do wyświetlenia przy zdjęciu.
    IReadOnlyList<AttractionPhoto> Photos,
    // Oznaczenie źródła danych do pokazania w interfejsie.
    string? Attribution);

public sealed record AttractionComponent(Guid Id, string Name);
