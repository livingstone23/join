namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Body of the activity create and update endpoints (<c>CalendarEvents</c> and <c>CalendarPanel</c>, SPEC 44).
/// Times arrive as ISO 8601 with offset (<see cref="Start"/>/<see cref="End"/>), or as dates for an all-day
/// activity (<see cref="StartDate"/>/<see cref="EndDate"/>, inclusive).
/// </summary>
public interface ICalendarEventInput
{
    Guid? CalendarId { get; }
    Guid CalendarActivityCompanyId { get; }
    string Title { get; }
    string? Summary { get; }
    string? Description { get; }
    string? Location { get; }
    string? VideoCallUrl { get; }
    bool IsAllDay { get; }
    DateTimeOffset? Start { get; }
    DateTimeOffset? End { get; }
    DateOnly? StartDate { get; }
    DateOnly? EndDate { get; }
    string? RecurrenceRule { get; }
    Guid? RequesterPersonId { get; }
    Guid? CustomerId { get; }
    Guid? TicketId { get; }
    Guid? ProjectId { get; }
}
