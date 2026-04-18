


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for ticket lifecycle states (e.g., Draft, Assigned, Resolved).
/// Allows dynamic workflow management and multi-language support.
/// </summary>
public class TicketStatus : BaseTenantEntity
{

    
    /// <summary>
    /// Name of the status (e.g., "Assigned", "Resolved"). This is the primary identifier for the status and should be unique within the system.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the status, its purpose, and any specific rules or conditions associated with it.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Numeric code used by integrations and internal workflow logic to reference the ticket status.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Indicates whether the ticket status is currently active and available for use in new workflow transitions.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Indicates whether this status is the initial state for the company workflow.
    /// </summary>
    public bool IsInitial { get; set; } = false;

    /// <summary>
    /// Indicates whether this status represents a paused workflow state.
    /// </summary>
    public bool IsPaused { get; set; } = false;

    /// <summary>
    /// Indicates whether this status is the final state for the company workflow.
    /// </summary>
    public bool IsFinal { get; set; } = false;

    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

}
