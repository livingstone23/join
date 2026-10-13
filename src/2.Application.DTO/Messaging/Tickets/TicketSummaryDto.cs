namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Aggregated counts of the current tenant's open ticket inbox (SPEC 52).
/// Drives the inbox summary cards and the sidenav badge.
/// </summary>
/// <remarks>
/// All counts ignore tickets with <c>GcRecord &gt; 0</c> and any ticket
/// whose status has <c>IsFinal = true</c>; the summary reflects the
/// workload of the company, not the historical record. <see cref="SlaBreachedCount"/>
/// and <see cref="InactiveCount"/> are computed in C# via
/// <c>TicketSlaCalculator</c> with the same inputs <c>GetTickets</c> uses, so
/// the numbers and the grid columns cannot drift apart.
/// </remarks>
public record TicketSummaryDto
{
    /// <summary>
    /// Total tickets in a non-final status for the active tenant.
    /// </summary>
    public int OpenCount { get; init; }

    /// <summary>
    /// Open tickets whose SLA reference instant has passed
    /// (<c>TicketSlaCalculator.Compute(..., isFinalStatus: false)</c>).
    /// </summary>
    public int SlaBreachedCount { get; init; }

    /// <summary>
    /// Open tickets whose last activity is older than the tenant's
    /// <c>TicketCompanyDefaults.MaxDayTicketInactivity</c> threshold.
    /// Zero when the tenant has no threshold configured.
    /// </summary>
    public int InactiveCount { get; init; }

    /// <summary>
    /// Open tickets without an assignee (<c>AssignedToUserId IS NULL</c>).
    /// </summary>
    public int UnassignedCount { get; init; }

    /// <summary>
    /// UTC instant at which the snapshot was computed. Returned so the front
    /// can show "actualizado a las HH:mm" without re-querying.
    /// </summary>
    public DateTime GeneratedAt { get; init; }
}