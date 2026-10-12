using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;

/// <summary>
/// Creates an activity in another user's calendar (<c>calendarId</c> optional → the owner's default). R5 applies.
/// </summary>
public record CreateCalendarPanelEventCommand : CalendarEventWriteCommand
{
    public Guid OwnerUserId { get; init; }
}
