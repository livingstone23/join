using System.Linq.Expressions;
using JOIN.Application.Interface.Persistence.Calendars;
using JOIN.Domain.Calendars;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Calendars;

/// <summary>
/// EF Core repository for <see cref="CalendarEvent"/> (SPEC 44).
/// </summary>
public sealed class CalendarEventRepository(ApplicationDbContext context)
    : GenericRepository<CalendarEvent>(context), ICalendarEventRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEventScheduleRow>> GetScheduleRowsAsync(
        Guid companyId,
        Guid ownerUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var events = _context.CalendarEvents
            .IgnoreQueryFilters()
            .Where(e => e.CompanyId == companyId && e.OwnerUserId == ownerUserId && e.GcRecord == 0);

        var masterIds = events
            .Where(e => e.RecurrenceRule != null
                && e.SeriesMasterId == null
                && e.StartUtc < toUtc
                && (e.RecurrenceEndUtc == null || e.RecurrenceEndUtc > fromUtc))
            .Select(e => e.Id);

        var rows = events.Where(e =>
            (e.RecurrenceRule == null && e.SeriesMasterId == null && e.StartUtc < toUtc && e.EndUtc > fromUtc)
            || masterIds.Contains(e.Id)
            || (e.SeriesMasterId != null && masterIds.Contains(e.SeriesMasterId.Value)));

        return await rows
            .Select(e => new CalendarEventScheduleRow(
                e.Id,
                e.SeriesMasterId,
                e.OriginalStartUtc,
                e.Title,
                e.StartUtc,
                e.EndUtc,
                e.IsAllDay,
                e.RecurrenceRule,
                e.RecurrenceEndUtc,
                e.CalendarActivityCompany.AllowsOverlap,
                e.CalendarStatusCompany.CalendarStatus.BlocksTime))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> GetExceptionsAsync(Guid seriesMasterId, CancellationToken cancellationToken = default) =>
        await _context.CalendarEvents
            .IgnoreQueryFilters()
            .Where(e => e.SeriesMasterId == seriesMasterId && e.GcRecord == 0)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> CountUpcomingActiveEventsInCalendarAsync(Guid calendarId, DateTime nowUtc, CancellationToken cancellationToken = default) =>
        _context.CalendarEvents
            .IgnoreQueryFilters()
            .Where(e => e.CalendarId == calendarId
                && e.GcRecord == 0
                && e.CalendarStatusCompany.CalendarStatus.Code != CalendarCodes.Statuses.Cancelled
                && (e.EndUtc > nowUtc
                    || (e.RecurrenceRule != null && (e.RecurrenceEndUtc == null || e.RecurrenceEndUtc > nowUtc))))
            .CountAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> AnyAsync(Expression<Func<CalendarEvent, bool>> predicate, CancellationToken cancellationToken = default) =>
        _context.CalendarEvents
            .IgnoreQueryFilters()
            .AnyAsync(predicate, cancellationToken);
}
