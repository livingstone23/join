


using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;



namespace JOIN.Domain.Support;



/// <summary>
/// Records every outbound notification sent to users or customers.
/// Supports both Ticket-specific alerts and Generic System alerts.
/// </summary>
public class TicketNotification : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key to the associated Ticket. 
    /// Nullable to support generic notifications (e.g., "You have 5 pending tasks").
    /// </summary>
    public Guid? TicketId { get; set; }

    /// <summary>
    /// Foreign key to the channel used for this notification (WhatsApp, SendGrid, etc.).
    /// </summary>
    public Guid CommunicationChannelId { get; set; }

    /// <summary>
    /// The actual content or template name sent.
    /// </summary>
    public string MessageSummary { get; set; } = string.Empty;

    /// <summary>
    /// External Reference ID from the provider (e.g., SendGrid Message ID).
    /// </summary>
    public string? ExternalProviderId { get; set; }

    /// <summary>
    /// Type of notification (e.g., "TicketAlert", "GenericReminder").
    /// </summary>
    public string NotificationType { get; set; } = "General";

    /// <summary>
    /// Timestamp of when the notification was successfully processed by the provider.
    /// </summary>
    public DateTime? SentAt { get; set; }

    // --- Navigation Properties ---
    public virtual Ticket? Ticket { get; set; }
    public virtual CommunicationChannel Channel { get; set; } = null!;
}