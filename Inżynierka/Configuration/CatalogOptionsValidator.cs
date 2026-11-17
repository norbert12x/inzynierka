using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Inżynierka.Configuration;

// Sprawdzane przy starcie aplikacji (ValidateOnStart): błędna konfiguracja zatrzymuje start
// z czytelnym komunikatem zamiast błędnych wyników wyszukiwania później.
public sealed partial class CatalogOptionsValidator : IValidateOptions<CatalogOptions>
{
    // Te same granice co parametr radiusKm w GET /api/attractions.
    public const double MinRadiusKm = 0.1;
    public const double MaxRadiusKm = 100;

    public ValidateOptionsResult Validate(string? name, CatalogOptions options)
    {
        var errors = GetErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> GetErrors(CatalogOptions options)
    {
        var errors = new List<string>();
        var regions = options.Regions;
        if (regions.Count == 0)
        {
            errors.Add("Catalog:Regions: skonfiguruj co najmniej jeden obszar.");
            return errors;
        }

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var prefix = $"Catalog:Regions:{i}";
            if (!CodePattern().IsMatch(region.Code))
                errors.Add($"{prefix}:Code: podaj kod z małych liter, cyfr i myślników (np. \"rzeszow\").");
            if (string.IsNullOrWhiteSpace(region.Name))
                errors.Add($"{prefix}:Name: podaj nazwę obszaru.");
            if (!double.IsFinite(region.CenterLatitude) || region.CenterLatitude is < -90 or > 90)
                errors.Add($"{prefix}:CenterLatitude: wartość od -90 do 90.");
            if (!double.IsFinite(region.CenterLongitude) || region.CenterLongitude is < -180 or > 180)
                errors.Add($"{prefix}:CenterLongitude: wartość od -180 do 180.");
            if (!double.IsFinite(region.RadiusKm) || region.RadiusKm is < MinRadiusKm or > MaxRadiusKm)
                errors.Add($"{prefix}:RadiusKm: wartość od {MinRadiusKm} do {MaxRadiusKm} km.");
        }

        var duplicates = regions.GroupBy(r => r.Code).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            errors.Add($"Catalog:Regions: powtórzone kody obszarów: {string.Join(", ", duplicates)}.");

        var defaults = regions.Count(r => r.IsDefault);
        if (defaults != 1)
            errors.Add($"Catalog:Regions: dokładnie jeden obszar musi mieć IsDefault=true (teraz: {defaults}).");

        return errors;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();
}
