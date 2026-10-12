namespace JOIN.Domain.Calendars.Enums;

/// <summary>
/// Scope of an update, status change or delete on a recurring <see cref="CalendarEvent"/>.
/// The occurrence is identified by its original start in UTC.
/// </summary>
public enum CalendarRecurrenceEditScope
{
    /// <summary>
    /// Creates or updates an exception (<see cref="CalendarEvent.SeriesMasterId"/> +
    /// <see cref="CalendarEvent.OriginalStartUtc"/>) for a single occurrence.
    /// </summary>
    ThisOccurrence = 1,

    /// <summary>
    /// Cuts the original series right before the occurrence and creates a new master from it.
    /// </summary>
    ThisAndFollowing = 2,

    /// <summary>
    /// Modifies the master; existing exceptions are kept and revalidated.
    /// </summary>
    Series = 3
}
