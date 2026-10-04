namespace Inżynierka.Models;

public static class AttractionCategoryCatalog
{
    public static IReadOnlyList<AttractionCategory> All { get; } = Array.AsReadOnly<AttractionCategory>(
    [
        new("museums", "Muzea i galerie"),
        new("heritage", "Zabytki i historia"),
        new("religious-sites", "Obiekty sakralne"),
        new("nature", "Przyroda"),
        new("viewpoints", "Punkty widokowe"),
        new("recreation", "Rozrywka i rekreacja"),
        new("culture", "Kultura"),
        new("public-art", "Sztuka w przestrzeni publicznej"),
        new("other", "Pozostałe atrakcje")
    ]);
}
