using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;

/// <summary>
/// Validates <see cref="DeleteUserCalendarCommand"/>.
/// </summary>
public sealed class DeleteUserCalendarCommandValidator : AbstractValidator<DeleteUserCalendarCommand>
{
    public DeleteUserCalendarCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The calendar id is required.");
    }
}
