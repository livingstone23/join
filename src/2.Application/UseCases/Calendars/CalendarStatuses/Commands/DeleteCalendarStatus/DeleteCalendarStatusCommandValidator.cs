using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;

/// <summary>
/// Validates <see cref="DeleteCalendarStatusCommand"/>.
/// </summary>
public sealed class DeleteCalendarStatusCommandValidator : AbstractValidator<DeleteCalendarStatusCommand>
{
    public DeleteCalendarStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The status id is required.");
    }
}
