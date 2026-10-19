using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// Row shape returned by <see cref="CalendarActivityIndexReader"/>: a stored activity (master, one-time, or
/// exception) with the columns the index needs for filtering, expansion and the
/// <see cref="CalendarActivityIndexItemDto"/> projection. Implements
/// <see cref="ICalendarEventSlot"/> so <see cref="CalendarOccurrenceExpansion"/> can reuse it.
/// </summary>
public sealed record CalendarActivityIndexRow : ICalendarEventSlot
{
    public Guid Id { get; init; }
    public Guid? SeriesMasterId { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public bool IsAllDay { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public string? RecurrenceRule { get; init; }

    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Location { get; init; }

    public Guid ActivityCompanyId { get; init; }
    public string ActivityName { get; init; } = string.Empty;
    public string ActivityColor { get; init; } = string.Empty;

    public Guid StatusCompanyId { get; init; }
    public string StatusCode { get; init; } = string.Empty;
    public string StatusName { get; init; } = string.Empty;
    public string StatusColor { get; init; } = string.Empty;

    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;

    public Guid CalendarId { get; init; }
    public string CalendarName { get; init; } = string.Empty;

    public Guid? RequesterPersonId { get; init; }
    public string? RequesterPersonName { get; init; }
    public string? CustomerCode { get; init; }

    public Guid OriginChannelId { get; init; }
    public string OriginChannelCode { get; init; } = string.Empty;
}