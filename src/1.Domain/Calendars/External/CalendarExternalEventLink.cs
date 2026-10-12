using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Link between a JOIN <see cref="CalendarEvent"/> and its external counterpart
/// (table <c>Calendar.ExternalEventLinks</c>, SPEC 46-post). One link per <c>(ConnectionId, CalendarEventId)</c>
/// and one per <c>(ConnectionId, ExternalEventId)</c>.
/// </summary>
public class CalendarExternalEventLink : BaseTenantEntity
{
    public Guid CalendarEventId { get; set; }

    public Guid ConnectionId { get; set; }

    /// <summary> Id of the event at the external provider. </summary>
    public string ExternalEventId { get; set; } = string.Empty;

    /// <summary> Stable <c>iCalUID</c> reported by the provider (stable across calendars). </summary>
    public string? ExternalICalUid { get; set; }

    /// <summary> <c>etag</c> of the last known version (used for conditional updates). </summary>
    public string? ExternalEtag { get; set; }

    /// <summary> <c>updated</c> reported by the provider in the last read. </summary>
    public DateTime? ExternalUpdatedUtc { get; set; }

    /// <summary> UTC moment of the last successful sync of this link. </summary>
    public DateTime? LastSyncedUtc { get; set; }

    public CalendarExternalSyncState SyncState { get; set; }

    /// <summary> Last error (no sensitive data). </summary>
    public string? LastError { get; set; }

    // --- Navigation ---
    public virtual CalendarEvent CalendarEvent { get; set; } = null!;
    public virtual CalendarExternalConnection Connection { get; set; } = null!;
}