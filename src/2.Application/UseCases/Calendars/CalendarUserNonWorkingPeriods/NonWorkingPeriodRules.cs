using FluentValidation;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods;

/// <summary>
/// Body of the non-working period endpoints.
/// </summary>
public interface INonWorkingPeriodInput
{
    DateOnly StartDate { get; }
    DateOnly EndDate { get; }
    CalendarNonWorkingReason Reason { get; }
    string? Notes { get; }
}

/// <summary>
/// Shared FluentValidation rules of <see cref="INonWorkingPeriodInput"/>.
/// </summary>
public static class NonWorkingPeriodRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : INonWorkingPeriodInput
    {
        validator.RuleFor(x => x.StartDate).NotEqual(default(DateOnly)).WithMessage("startDate is required.");
        validator.RuleFor(x => x.EndDate).NotEqual(default(DateOnly)).WithMessage("endDate is required.")
            .GreaterThanOrEqualTo(x => x.StartDate).WithMessage("endDate must be on or after startDate.");
        validator.RuleFor(x => x.Reason).IsInEnum().WithMessage("reason must be Vacation (1), Leave (2), Absence (3) or Other (99).");
        validator.RuleFor(x => x.Notes).MaximumLength(500).WithMessage("The notes cannot exceed 500 characters.");
    }
}
