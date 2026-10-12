namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Status of a <see cref="CalendarSyncOutboxItem"/>. Only one row in <c>Pending</c> may exist per
/// <c>(ConnectionId, CalendarEventId)</c> (filtered unique index, SPEC 46-post).
/// </summary>
public enum CalendarSyncOutboxStatus
{
    Pending = 1,
    Processing = 2,
    Done = 3,
    Failed = 4,
    Superseded = 5
}