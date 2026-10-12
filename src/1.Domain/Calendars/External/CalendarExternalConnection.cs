using JOIN.Domain.Audit;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Calendars;

/// <summary>
/// Account of an external calendar provider (Google, later Outlook) linked by a JOIN user within a
/// company (table <c>Calendar.ExternalConnections</c>, SPEC 46-post). One active row per
/// <c>(CompanyId, UserId, Provider)</c>. Cross-schema FK: <c>UserId</c> resolves to <c>Security.Users</c>.
/// </summary>
public class CalendarExternalConnection : BaseTenantEntity
{
    /// <summary> Owner of the connection (JOIN user). </summary>
    public Guid UserId { get; set; }

    public CalendarExternalProvider Provider { get; set; }

    /// <summary> Account e-mail at the external provider (shown to the user). </summary>
    public string ExternalAccountEmail { get; set; } = string.Empty;

    /// <summary> Target external calendar id. Defaults to <c>"primary"</c> (the user's main calendar). </summary>
    public string ExternalCalendarId { get; set; } = "primary";

    /// <summary>
    /// JOIN calendar that is being synced. <c>null</c> = all calendars of the user in this company.
    /// </summary>
    public Guid? SourceCalendarId { get; set; }

    /// <summary>
    /// Refresh token encrypted with ASP.NET Core Data Protection (purpose
    /// <c>Calendar.ExternalConnection.RefreshToken</c>). Never returned by API or written to logs.
    /// </summary>
    public string EncryptedRefreshToken { get; set; } = string.Empty;

    /// <summary> Scopes granted by the user at the OAuth consent screen. </summary>
    public string GrantedScopes { get; set; } = string.Empty;

    public CalendarExternalConnectionStatus Status { get; set; }

    /// <summary> UTC moment of the linking. Only activities with <c>Created &gt;= ConnectedUtc</c> are synced. </summary>
    public DateTime ConnectedUtc { get; set; }

    /// <summary> UTC moment of the last successful sync round-trip. </summary>
    public DateTime? LastSyncUtc { get; set; }

    /// <summary> Last error message (no sensitive data). </summary>
    public string? LastError { get; set; }

    /// <summary> Consecutive failures; reaching the limit flips the connection to <see cref="CalendarExternalConnectionStatus.Error"/>. </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary> <c>nextSyncToken</c> returned by the provider for incremental inbound reads. </summary>
    public string? InboundSyncToken { get; set; }

    /// <summary> Push-notification channel id (<c>events.watch</c>). </summary>
    public string? WatchChannelId { get; set; }

    /// <summary> <c>resourceId</c> returned by the provider (needed to stop the channel). </summary>
    public string? WatchResourceId { get; set; }

    /// <summary> Our own token re-sent by the provider on every push notification (origin validation). </summary>
    public string? WatchChannelToken { get; set; }

    /// <summary> UTC moment the push channel expires. </summary>
    public DateTime? WatchExpiresUtc { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual UserCalendar? SourceCalendar { get; set; }
}