using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;

/// <summary>
/// Validates <see cref="DeleteMyNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class DeleteMyNonWorkingPeriodCommandValidator : AbstractValidator<DeleteMyNonWorkingPeriodCommand>
{
    public DeleteMyNonWorkingPeriodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The period id is required.");
    }
}
