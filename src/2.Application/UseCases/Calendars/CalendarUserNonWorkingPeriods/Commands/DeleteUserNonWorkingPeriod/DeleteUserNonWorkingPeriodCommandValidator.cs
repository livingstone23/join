using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;

/// <summary>
/// Validates <see cref="DeleteUserNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class DeleteUserNonWorkingPeriodCommandValidator : AbstractValidator<DeleteUserNonWorkingPeriodCommand>
{
    public DeleteUserNonWorkingPeriodCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The period id is required.");
    }
}
