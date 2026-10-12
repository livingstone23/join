namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Operation a <see cref="CalendarSyncOutboxItem"/> instructs the worker to perform against the
/// external provider.
/// </summary>
public enum CalendarSyncOperation
{
    Upsert = 1,
    Delete = 2
}