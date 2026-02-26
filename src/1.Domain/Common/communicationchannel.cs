


using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;



namespace JOIN.Domain.Common;



/// <summary>
/// Represents a communication platform integrated with the system (e.g., WhatsApp, Telegram, SendGrid, Web).
/// Used to identify both the source of a ticket and the medium for notifications.
/// </summary>
public class CommunicationChannel : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the name of the channel (e.g., "WhatsApp Business", "Customer Portal").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the technical provider or agent handling the channel (e.g., "n8n", "Twilio", "Internal").
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Internal code to facilitate logic in the Application layer (e.g., WHATSAPP_01).
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Indicates if the channel is enabled for sending and receiving messages.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // --- Navigation Properties ---
    
    /// <summary> Tickets created through this channel. </summary>
    public virtual ICollection<Ticket> CreatedTickets { get; set; } = new List<Ticket>();
    
}