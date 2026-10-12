using System.Linq.Expressions;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.Interface.Persistence.Calendars;

/// <summary>
/// EF Core repository for <see cref="CalendarEvent"/> with the reads the calendar commands need (SPEC 44).
/// Reads run on the command's own DbContext connection, so they see the rows written earlier in the same
/// transaction and the rows committed before the schedule lock was taken. Every method bypasses the global
/// query filters and scopes by the arguments it receives.
/// </summary>
public interface ICalendarEventRepository : IGenericRepository<CalendarEvent>
{
    /// <summary>
    /// Returns the owner's activities that can occupy time in <c>[fromUtc, toUtc)</c>: one-time activities that
    /// intersect the range, series masters that can produce occurrences in it, and every active exception of those
    /// masters. Each row carries the effective <c>AllowsOverlap</c> of its type and <c>BlocksTime</c> of its status.
    /// </summary>
    Task<IReadOnlyList<CalendarEventScheduleRow>> GetScheduleRowsAsync(
        Guid companyId,
        Guid ownerUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the tracked, active exceptions of a series master.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> GetExceptionsAsync(Guid seriesMasterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the active, non-cancelled activities of a calendar that end after <paramref name="nowUtc"/>
    /// (series without end or ending later included).
    /// </summary>
    Task<int> CountUpcomingActiveEventsInCalendarAsync(Guid calendarId, DateTime nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether any activity matches the predicate, bypassing the global query filters. Callers scope it.
    /// </summary>
    Task<bool> AnyAsync(Expression<Func<CalendarEvent, bool>> predicate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Time slot of an existing activity used by the overlap validation and the occurrence expansion (SPEC 44 R4).
/// </summary>
public sealed record CalendarEventScheduleRow(
    Guid Id,
    Guid? SeriesMasterId,
    DateTime? OriginalStartUtc,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAllDay,
    string? RecurrenceRule,
    DateTime? RecurrenceEndUtc,
    bool AllowsOverlap,
    bool BlocksTime) : ICalendarEventSlot;
