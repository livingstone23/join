using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;

/// <summary>
/// Logically deletes an activity of the current user (records created by mistake; to cancel use the status).
/// </summary>
public record DeleteCalendarEventCommand(Guid Id, CalendarRecurrenceEditScope? Scope = null, DateTime? OccurrenceStartUtc = null)
    : ITransactionalCommand<Response<bool>>, ICalendarEventTarget;
