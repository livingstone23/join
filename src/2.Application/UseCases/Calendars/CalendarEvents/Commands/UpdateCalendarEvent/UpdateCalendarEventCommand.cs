using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.UpdateCalendarEvent;

/// <summary>
/// Updates an activity of the current user. For a series, <c>scope</c> and <c>occurrenceStartUtc</c> are required.
/// </summary>
public record UpdateCalendarEventCommand : CalendarEventUpdateCommand;
