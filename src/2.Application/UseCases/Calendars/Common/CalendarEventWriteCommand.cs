using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Body shared by the activity create and update commands of <c>CalendarEvents</c> and <c>CalendarPanel</c>.
/// </summary>
public abstract record CalendarEventWriteCommand : ITransactionalCommand<Response<CalendarEventDto>>, ICalendarEventInput
{
    public Guid? CalendarId { get; init; }
    public Guid CalendarActivityCompanyId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? VideoCallUrl { get; init; }
    public bool IsAllDay { get; init; }
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? RecurrenceRule { get; init; }
    public Guid? RequesterPersonId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? TicketId { get; init; }
    public Guid? ProjectId { get; init; }
}

/// <summary>
/// Route and query values of the commands that target an existing activity (<c>{id}?scope&amp;occurrenceStartUtc</c>).
/// </summary>
public interface ICalendarEventTarget
{
    Guid Id { get; }
    CalendarRecurrenceEditScope? Scope { get; }
    DateTime? OccurrenceStartUtc { get; }
}

/// <summary>
/// Update body plus the target of the activity.
/// </summary>
public abstract record CalendarEventUpdateCommand : CalendarEventWriteCommand, ICalendarEventTarget
{
    [JsonIgnore]
    public Guid Id { get; init; }

    [JsonIgnore]
    public CalendarRecurrenceEditScope? Scope { get; init; }

    [JsonIgnore]
    public DateTime? OccurrenceStartUtc { get; init; }
}
