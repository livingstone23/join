using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;

/// <summary>
/// Creates an activity in a calendar of the current user (<c>calendarId</c> optional → the default calendar).
/// The status is not accepted: it comes from the activity type (R6).
/// </summary>
public record CreateCalendarEventCommand : CalendarEventWriteCommand;
