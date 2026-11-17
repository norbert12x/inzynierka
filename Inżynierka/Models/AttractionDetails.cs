using Inżynierka.Planning;

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
    // Czas wizyty w minutach i jego źródło: "place" (zapisany przy atrakcji)
    // albo "category_default" (szacunek – najdłuższy czas spośród kategorii).
    int VisitMinutes,
    VisitMinutesSource VisitMinutesSource,
    // Elementy obiektu (np. tablice, rzeźby w parku) przypisane do tej atrakcji.
    IReadOnlyList<AttractionComponent> Components,
    // Tylko zatwierdzone zdjęcia samej atrakcji, z autorem i licencją do wyświetlenia przy zdjęciu.
    IReadOnlyList<AttractionPhoto> Photos,
    // Zatwierdzone zdjęcia eksponatów i wnętrz (zwykle muzea), z podpisem `Caption`; pusta lista = brak.
    IReadOnlyList<AttractionPhoto> Exhibits,
    // Oznaczenie źródła danych do pokazania w interfejsie.
    string? Attribution);

public sealed record AttractionComponent(Guid Id, string Name);
