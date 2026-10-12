using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;

/// <summary>
/// Validates <see cref="CreateCalendarEventCommand"/>.
/// </summary>
public sealed class CreateCalendarEventCommandValidator : AbstractValidator<CreateCalendarEventCommand>
{
    public CreateCalendarEventCommandValidator() => CalendarEventInputRules.Apply(this);
}
