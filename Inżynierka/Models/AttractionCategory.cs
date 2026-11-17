namespace Inżynierka.Models;

// Code jest stałym identyfikatorem; Name to nazwa wyświetlana użytkownikowi.
public sealed record AttractionCategory(string Code, string Name);

// Pozycja słownika z GET /api/attraction-categories, z domyślnym czasem wizyty w minutach.
public sealed record AttractionCategoryInfo(string Code, string Name, int DefaultVisitMinutes);
