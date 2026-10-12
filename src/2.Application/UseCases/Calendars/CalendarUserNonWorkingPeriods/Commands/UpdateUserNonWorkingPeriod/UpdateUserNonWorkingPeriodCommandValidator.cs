using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;

/// <summary>
/// Validates <see cref="UpdateUserNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class UpdateUserNonWorkingPeriodCommandValidator : AbstractValidator<UpdateUserNonWorkingPeriodCommand>
{
    public UpdateUserNonWorkingPeriodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The period id is required.");
        NonWorkingPeriodRules.Apply(this);
    }
}
