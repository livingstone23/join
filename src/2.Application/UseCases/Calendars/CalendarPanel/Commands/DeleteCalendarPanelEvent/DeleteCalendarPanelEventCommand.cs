using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;

/// <summary>
/// Logically deletes an activity of another user of the company.
/// </summary>
public record DeleteCalendarPanelEventCommand(Guid Id, CalendarRecurrenceEditScope? Scope = null, DateTime? OccurrenceStartUtc = null)
    : ITransactionalCommand<Response<bool>>, ICalendarEventTarget;
