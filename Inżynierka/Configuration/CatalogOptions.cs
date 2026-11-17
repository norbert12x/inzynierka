namespace Inżynierka.Configuration;

// Sekcja "Catalog" w appsettings.json. Obszary są danymi konfiguracji, nie stałymi w kodzie,
// żeby można było dodać kolejne części Podkarpacia bez zmian w logice.
public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    public List<RegionDefinition> Regions { get; set; } = [];
}

public sealed class RegionDefinition
{
    // Stały identyfikator, np. "rzeszow"; małe litery, cyfry i myślniki.
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public double CenterLatitude { get; set; }
    public double CenterLongitude { get; set; }
    public double RadiusKm { get; set; }
    // Obszar wybierany na start, gdy użytkownik jeszcze nic nie wskazał. Dokładnie jeden.
    public bool IsDefault { get; set; }
}
