using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Maps a User to a specific communication channel (WhatsApp, Telegram, etc.).
/// Crucial for routing automated alerts and notifications to the correct agent/user endpoint.
/// </summary>
public class UserCommunicationChannel : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Foreign key to the CommunicationChannel (e.g., WhatsApp, Telegram catalog).
    /// </summary>
    public Guid CommunicationChannelId { get; set; }

    /// <summary>
    /// The unique identifier for the user on this channel (e.g., Phone Number for WA, @username for Telegram).
    /// </summary>
    public string ChannelIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if this is the preferred channel for urgent notifications.
    /// </summary>
    public bool IsPreferred { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual CommunicationChannel Channel { get; set; } = null!;
}
