


using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Represents a service request with advanced tracking, SLAs, and multi-channel support.
/// </summary>
public class Ticket : BaseAuditableEntity
{
    /// <summary> Human-readable identifier (e.g., 2006_001). </summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // --- Time & Metrics ---
    public Guid TimeUnitId { get; set; }
    public decimal EstimatedTime { get; set; } // Tiempo proyectado
    public decimal ConsumedTime { get; set; }  // Tiempo consumido

    // --- Status & Complexity (Tables) ---
    public Guid TicketStatusId { get; set; }
    public Guid TicketComplexityId { get; set; }

    // --- Context & Hierarchy ---
    public Guid CompanyId { get; set; } // Requerido
    public Guid? ProjectId { get; set; } // Opcional
    public Guid? AreaId { get; set; }    // Opcional
    public Guid ChannelId { get; set; } // Canal de creación (Bot, Web, etc.)
    
    /// <summary> Link to the parent ticket if this is a follow-up. </summary>
    public Guid? PrecedentTicketId { get; set; }

    /// <summary>
    /// Guide to indicate if user that are not created, or user can watch the ticket
    /// </summary>
    /// <value></value>   
    public bool IsVisibleToExternals { get; set; } = false;

    // --- Ownership ---
    public string CreatedByUserId { get; set; } = string.Empty; // Usuario creó
    public string? AssignedToUserId { get; set; } // Usuario gestiona

    // --- Navigation ---
    public virtual Company Company { get; set; } = null!;
    public virtual Customer Customer { get; set; } = null!;
    public virtual TicketStatus Status { get; set; } = null!;
    public virtual TicketComplexity Complexity { get; set; } = null!;
    public virtual TimeUnit TimeUnit { get; set; } = null!;
    public virtual CommunicationChannel CreationChannel { get; set; } = null!;
    public virtual Ticket? PrecedentTicket { get; set; }
    public virtual ICollection<TicketNotification> Notifications { get; set; } = new List<TicketNotification>();
}