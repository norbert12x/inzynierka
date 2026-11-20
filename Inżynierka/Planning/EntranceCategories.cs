namespace Inżynierka.Planning;

// Kategorie obiektów z wejściem (zwykle z godzinami otwarcia i biletami) – dla nich brak godzin
// w katalogu daje ostrzeżenie w planie. Zabytki, przyroda, obiekty sakralne, punkty widokowe, sztuka
// w przestrzeni publicznej – nie (najczęściej dostępne bez ograniczeń). Lista do konfiguracji w 4.6.
public static class EntranceCategories
{
    public static readonly IReadOnlySet<string> DefaultCodes =
        new HashSet<string>(["museums", "recreation", "culture", "active-entertainment"]);

    // Wystarczy jedna kategoria z wejściem (np. muzeum w zamku).
    public static bool HasEntrance(IEnumerable<string> categoryCodes, IReadOnlySet<string>? entranceCodes = null)
        => categoryCodes.Any((entranceCodes ?? DefaultCodes).Contains);
}
