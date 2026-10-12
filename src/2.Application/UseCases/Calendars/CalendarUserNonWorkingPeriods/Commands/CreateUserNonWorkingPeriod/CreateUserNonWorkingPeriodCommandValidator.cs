using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;

/// <summary>
/// Validates <see cref="CreateUserNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class CreateUserNonWorkingPeriodCommandValidator : AbstractValidator<CreateUserNonWorkingPeriodCommand>
{
    public CreateUserNonWorkingPeriodCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("The user id is required.");
        NonWorkingPeriodRules.Apply(this);
    }
}
