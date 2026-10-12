using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;

/// <summary>
/// Validates <see cref="SetDefaultUserCalendarCommand"/>.
/// </summary>
public sealed class SetDefaultUserCalendarCommandValidator : AbstractValidator<SetDefaultUserCalendarCommand>
{
    public SetDefaultUserCalendarCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The calendar id is required.");
    }
}
