using Microsoft.Extensions.Options;

namespace Inżynierka.Configuration;

// Sprawdzane przy starcie (ValidateOnStart): błędne wartości domyślne planera zatrzymują aplikację.
public sealed class PlannerOptionsValidator : IValidateOptions<PlannerOptions>
{
    public ValidateOptionsResult Validate(string? name, PlannerOptions options)
    {
        var errors = GetErrors(options);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    public static IReadOnlyList<string> GetErrors(PlannerOptions options)
    {
        var errors = new List<string>();
        const string prefix = PlannerOptions.SectionName;

        if (options.DefaultDayEnd <= options.DefaultDayStart)
            errors.Add($"{prefix}:DefaultDayEnd: koniec dnia musi być po {prefix}:DefaultDayStart.");
        if (options.MealMinutes is < 10 or > 240)
            errors.Add($"{prefix}:MealMinutes: wartość od 10 do 240 min.");
        if (options.MealLatestStart < options.MealEarliestStart)
            errors.Add($"{prefix}:MealLatestStart: najpóźniejszy start posiłku nie może być przed {prefix}:MealEarliestStart.");
        if (options.OverrunToleranceMinutes is < 0 or > 180)
            errors.Add($"{prefix}:OverrunToleranceMinutes: wartość od 0 do 180 min.");
        if (options.MaxSuggestionsPerDay is < 0 or > 10)
            errors.Add($"{prefix}:MaxSuggestionsPerDay: wartość od 0 do 10.");
        if (!double.IsFinite(options.CandidateMaxDistanceKm) || options.CandidateMaxDistanceKm is < 1 or > 100)
            errors.Add($"{prefix}:CandidateMaxDistanceKm: wartość od 1 do 100 km.");
        if (options.SmallAttractionMaxMinutes is < 0 or > 120)
            errors.Add($"{prefix}:SmallAttractionMaxMinutes: wartość od 0 do 120 min.");
        if (options.OnTheWayMaxDetourMinutes is < 0 or > 30)
            errors.Add($"{prefix}:OnTheWayMaxDetourMinutes: wartość od 0 do 30 min.");
        if (options.EntranceCategories is { } codes && codes.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{prefix}:EntranceCategories: puste kody kategorii.");

        return errors;
    }
}
