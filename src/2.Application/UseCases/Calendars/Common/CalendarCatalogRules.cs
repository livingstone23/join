using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Validation helpers shared by the calendar catalogs.
/// </summary>
public static class CalendarCatalogRules
{
    /// <summary> Hex color <c>#RRGGBB</c>. </summary>
    public const string ColorPattern = "^#[0-9A-Fa-f]{6}$";

    /// <summary> Technical code: letters, digits and underscore. </summary>
    public const string CodePattern = "^[A-Za-z0-9_]+$";

    public static IRuleBuilderOptions<T, string> ValidColor<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("The color is required.")
            .Matches(ColorPattern).WithMessage("The color must be a hex value like #1E88E5.");

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule, int maxLength) =>
        rule.NotEmpty().WithMessage("The name is required.")
            .MaximumLength(maxLength).WithMessage($"The name cannot exceed {maxLength} characters.");

    public static IRuleBuilderOptions<T, int> ValidDuration<T>(this IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(5, 1440).WithMessage("The default duration must be between 5 and 1440 minutes.");

    /// <summary> Normalized technical code (trimmed, upper case). </summary>
    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    /// <summary> <c>null</c> for blank text, trimmed text otherwise. </summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
