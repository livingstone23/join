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
    Deleted = 8,
    // SPEC 69 — comments and documents. The bit above 8 keeps room for the SPEC 44 set.
    CommentAdded = 9,
    DocumentAdded = 10,
    CommentDeleted = 11,
    DocumentDeleted = 12
}
