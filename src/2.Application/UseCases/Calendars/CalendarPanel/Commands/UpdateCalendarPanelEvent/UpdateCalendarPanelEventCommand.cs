using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;

/// <summary>
/// Updates an activity of another user of the company: R5 applies and a time change by a non-owner sets the
/// reschedule status of the company (R6).
/// </summary>
public record UpdateCalendarPanelEventCommand : CalendarEventUpdateCommand;
