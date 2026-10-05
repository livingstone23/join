namespace JOIN.Domain.Messaging;

/// <summary>
/// Pure calculator for ticket SLA, breach, and inactivity indicators.
/// No I/O, no ambient clock — <c>nowUtc</c> is always received as a parameter
/// so callers (and tests) stay in control of time.
/// </summary>
public static class TicketSlaCalculator
{
    /// <summary>
    /// Output of a single SLA computation for one ticket at one instant.
    /// </summary>
    /// <param name="SlaDueAt">When the ticket's SLA is due.</param>
    /// <param name="IsSlaBreached">Whether the SLA reference instant is past <paramref name="SlaDueAt"/>.</param>
    /// <param name="LastActivityAt">Last activity instant for the ticket (mirrored from input for caller convenience).</param>
    /// <param name="IsInactive">Whether the ticket is inactive according to the company's inactivity threshold.</param>
    public sealed record Result(DateTime SlaDueAt, bool IsSlaBreached, DateTime LastActivityAt, bool IsInactive);

    /// <summary>
    /// Computes the SLA, and inactivity indicators for a ticket.
    /// </summary>
    /// <param name="createdUtc">Ticket creation instant (UTC).</param>
    /// <param name="resolutionTimeUnits">Value from <see cref="TicketComplexity.ResolutionTimeUnits"/>.</param>
    /// <param name="complexityTimeUnitCode">
    /// Code of the <see cref="TimeUnit"/> pointed at by <see cref="TicketComplexity.TimeUnitId"/>
    /// (NOT the ticket's own <see cref="Ticket.TimeUnitId"/>).
    /// </param>
    /// <param name="lastActivityUtc">Instant of the latest <c>TicketLog</c> for the ticket (UTC).</param>
    /// <param name="finishedAtUtc">
    /// Instant of the <c>LogType.Finalization</c> log, if any. May be <c>null</c> for tickets
    /// closed before the audit log was wired or via a legacy path.
    /// </param>
    /// <param name="isFinalStatus">Whether the ticket is currently in a status flagged <c>IsFinal</c>.</param>
    /// <param name="maxDayTicketInactivity">Optional inactivity threshold in days for the company.</param>
    /// <param name="nowUtc">Reference instant used to evaluate breach and inactivity (UTC).</param>
    public static Result Compute(
        DateTime createdUtc,
        int resolutionTimeUnits,
        int complexityTimeUnitCode,
        DateTime lastActivityUtc,
        DateTime? finishedAtUtc,
        bool isFinalStatus,
        int? maxDayTicketInactivity,
        DateTime nowUtc)
    {
        var slaDueAt = createdUtc.AddHours(resolutionTimeUnits * (double)complexityTimeUnitCode);

        // A finished ticket is judged against when it actually finished, not "now" —
        // otherwise every closed ticket would eventually read as breached forever.
        var breachReferenceInstant = isFinalStatus ? (finishedAtUtc ?? lastActivityUtc) : nowUtc;
        var isSlaBreached = breachReferenceInstant > slaDueAt;

        var isInactive = !isFinalStatus
            && maxDayTicketInactivity.HasValue
            && (nowUtc - lastActivityUtc).TotalDays > maxDayTicketInactivity.Value;

        return new Result(slaDueAt, isSlaBreached, lastActivityUtc, isInactive);
    }
}