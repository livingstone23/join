using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Append-only audit row for security-relevant events (login, MFA, sessions, etc.).
/// Indexes by <c>(UserId, OccurredAtUtc DESC)</c> for the per-user activity feed.
/// No soft-delete: events never get rewritten; corrections produce new rows.
/// </summary>
public class SecurityEventLog : BaseEntity
{
    /// <summary>
    /// Affected user. Nullable so the row survives user deletion.
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Type of event (see <see cref="SecurityEventType"/>).
    /// </summary>
    public int EventType { get; set; }

    /// <summary>
    /// UTC instant at which the event was observed.
    /// </summary>
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>
    /// Originating IP (X-Forwarded-For first hop, otherwise RemoteIpAddress).
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Originating User-Agent header.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Outcome (see <see cref="SecurityEventResult"/>).
    /// </summary>
    public int Result { get; set; }

    /// <summary>
    /// Free-form JSON context (e.g. actor user id, role id). Optional.
    /// </summary>
    public string? MetadataJson { get; set; }

    /// <summary>
    /// Navigation reference. Nullable because <see cref="UserId"/> is nullable.
    /// </summary>
    public virtual ApplicationUser? User { get; set; }
}
