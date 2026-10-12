using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;

/// <summary>
/// Validates <see cref="CreateMyNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class CreateMyNonWorkingPeriodCommandValidator : AbstractValidator<CreateMyNonWorkingPeriodCommand>
{
    public CreateMyNonWorkingPeriodCommandValidator() => NonWorkingPeriodRules.Apply(this);
}
