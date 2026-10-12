using JOIN.Application.Interface;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Time data of a stored activity row (one-time activity, series master or exception).
/// </summary>
public interface ICalendarEventSlot
{
    Guid Id { get; }
    Guid? SeriesMasterId { get; }
    DateTime? OriginalStartUtc { get; }
    DateTime StartUtc { get; }
    DateTime EndUtc { get; }
    bool IsAllDay { get; }
    string? RecurrenceRule { get; }
}

/// <summary>
/// One occurrence produced by <see cref="CalendarOccurrenceExpansion"/>. <see cref="EventId"/> is the one-time
/// activity or the series master; <see cref="ExceptionId"/> is set when an exception replaced the occurrence;
/// <see cref="OccurrenceStartUtc"/> is the key of the occurrence in the series (its original start).
/// </summary>
public sealed record ExpandedOccurrence<T>(T Source, Guid EventId, Guid? ExceptionId, DateTime OccurrenceStartUtc, DateTime StartUtc, DateTime EndUtc)
    where T : ICalendarEventSlot
{
    /// <summary> Whether the occurrence belongs to a series. </summary>
    public bool IsRecurring => ExceptionId.HasValue || Source.RecurrenceRule is not null;

    /// <summary> Half-open interval overlap: touching ends do not overlap (SPEC 44 R4). </summary>
    public bool Overlaps(DateTime startUtc, DateTime endUtc) => StartUtc < endUtc && startUtc < EndUtc;
}

/// <summary>
/// Expands stored rows into the occurrences of <c>[fromUtc, toUtc)</c> (SPEC 44, "Implementación de las
/// consultas", step 3): one-time activities as they are, series through <see cref="ICalendarRecurrenceExpander"/>,
/// and each exception replacing the occurrence it keys (<c>SeriesMasterId</c> + <c>OriginalStartUtc</c>).
/// Shared by the overlap validation and every range query.
/// </summary>
public static class CalendarOccurrenceExpansion
{
    public static IReadOnlyList<ExpandedOccurrence<T>> Expand<T>(
        IEnumerable<T> rows,
        DateTime fromUtc,
        DateTime toUtc,
        string timeZoneId,
        ICalendarRecurrenceExpander expander)
        where T : ICalendarEventSlot
    {
        var list = rows.ToList();
        var exceptionKeys = list
            .Where(r => r.SeriesMasterId.HasValue && r.OriginalStartUtc.HasValue)
            .Select(r => (r.SeriesMasterId!.Value, CalendarTime.AsUtc(r.OriginalStartUtc!.Value)))
            .ToHashSet();

        var result = new List<ExpandedOccurrence<T>>();
        foreach (var row in list)
        {
            if (row.SeriesMasterId.HasValue)
            {
                if (Intersects(row.StartUtc, row.EndUtc, fromUtc, toUtc))
                {
                    result.Add(new ExpandedOccurrence<T>(
                        row,
                        row.SeriesMasterId.Value,
                        row.Id,
                        CalendarTime.AsUtc(row.OriginalStartUtc ?? row.StartUtc),
                        CalendarTime.AsUtc(row.StartUtc),
                        CalendarTime.AsUtc(row.EndUtc)));
                }

                continue;
            }

            if (row.RecurrenceRule is null)
            {
                if (Intersects(row.StartUtc, row.EndUtc, fromUtc, toUtc))
                {
                    result.Add(new ExpandedOccurrence<T>(row, row.Id, null, CalendarTime.AsUtc(row.StartUtc), CalendarTime.AsUtc(row.StartUtc), CalendarTime.AsUtc(row.EndUtc)));
                }

                continue;
            }

            var series = new CalendarRecurrenceSeries(row.StartUtc, row.EndUtc, row.RecurrenceRule, row.IsAllDay);
            foreach (var occurrence in expander.Expand(series, fromUtc, toUtc, timeZoneId))
            {
                if (exceptionKeys.Contains((row.Id, occurrence.StartUtc)))
                {
                    continue;
                }

                result.Add(new ExpandedOccurrence<T>(row, row.Id, null, occurrence.StartUtc, occurrence.StartUtc, occurrence.EndUtc));
            }
        }

        return result.OrderBy(o => o.StartUtc).ThenBy(o => o.EndUtc).ToList();
    }

    private static bool Intersects(DateTime startUtc, DateTime endUtc, DateTime fromUtc, DateTime toUtc) =>
        startUtc < toUtc && endUtc > fromUtc;
}
