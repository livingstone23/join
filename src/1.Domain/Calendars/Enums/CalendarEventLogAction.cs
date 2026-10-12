namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Operation recorded in a <see cref="CalendarEventLog"/> row.
/// </summary>
public enum CalendarEventLogAction
{
    Created = 1,
    Updated = 2,
    Rescheduled = 3,
    StatusChanged = 4,
    MovedToCalendar = 5,
    OccurrenceModified = 6,
    SeriesSplit = 7,
    Deleted = 8
}
