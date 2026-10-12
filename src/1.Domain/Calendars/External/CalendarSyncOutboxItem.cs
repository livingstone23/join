using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Pending change to push to the external provider (table <c>Calendar.SyncOutbox</c>, SPEC 46-post).
/// Written by the application handlers in the same transaction as the activity change; processed
/// later by the worker with exponential backoff. One <c>Pending</c> row per
/// <c>(ConnectionId, CalendarEventId)</c>.
/// </summary>
public class CalendarSyncOutboxItem : BaseTenantEntity
{
    public Guid CalendarEventId { get; set; }

    public Guid ConnectionId { get; set; }

    public CalendarSyncOperation Operation { get; set; }

    public CalendarSyncOutboxStatus Status { get; set; }

    public int Attempts { get; set; }

    /// <summary> Earliest UTC moment the worker may attempt this row again. </summary>
    public DateTime NextAttemptUtc { get; set; }

    /// <summary> UTC moment the row was enqueued. </summary>
    public DateTime EnqueuedUtc { get; set; }

    /// <summary> UTC moment the worker finished processing (success or terminal failure). </summary>
    public DateTime? ProcessedUtc { get; set; }

    /// <summary> UTC moment until which this row is locked by a worker instance (skipped by others). </summary>
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary> Last error (no sensitive data). </summary>
    public string? LastError { get; set; }

    // --- Navigation ---
    public virtual CalendarEvent CalendarEvent { get; set; } = null!;
    public virtual CalendarExternalConnection Connection { get; set; } = null!;
}