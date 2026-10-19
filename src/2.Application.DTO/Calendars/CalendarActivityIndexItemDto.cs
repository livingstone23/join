namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// One occurrence of the activity index (<c>GET /CalendarActivityIndex</c>, SPEC 68). <see cref="EventId"/> is the
/// master or the one-time activity; <see cref="ExceptionId"/> is set when the occurrence is an exception.
/// <see cref="OccurrenceStartUtc"/> is the key of the occurrence in the series.
/// </summary>
public record CalendarActivityIndexItemDto
{
    public Guid EventId { get; init; }
    public Guid? ExceptionId { get; init; }
    public DateTime OccurrenceStartUtc { get; init; }
    public DateTime OccurrenceEndUtc { get; init; }
    public bool IsAllDay { get; init; }
    public bool IsRecurring { get; init; }
    public string Title { get; init; } = string.Empty;
    public Guid ActivityCompanyId { get; init; }
    public string ActivityName { get; init; } = string.Empty;
    public string ActivityColor { get; init; } = string.Empty;
    public Guid StatusCompanyId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public string CalendarName { get; init; } = string.Empty;
    public Guid? RequesterPersonId { get; init; }
    public string? RequesterPersonName { get; init; }
    public string? CustomerCode { get; init; }
    public string OriginChannelCode { get; init; } = string.Empty;
    public string? Location { get; init; }
}