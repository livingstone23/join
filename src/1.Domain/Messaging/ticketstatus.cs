


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for ticket lifecycle states (e.g., Draft, Assigned, Resolved).
/// Allows dynamic workflow management and multi-language support.
/// </summary>
public class TicketStatus : BaseAuditableEntity
{

    /// <summary>
    /// Name of the status (e.g., "Assigned", "Resolved"). This is the primary identifier for the status and should be unique within the system.
    /// </summary>
    /// <value></value>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Detailed description of the status, its purpose, and any specific rules or conditions associated with it. This can be used for documentation and to provide clarity to users when selecting or transitioning to this status.
    /// </summary>
    /// <value></value>
    public string? Description { get; set; }
    
    /// <summary>
    /// Code or identifier for the status, which can be used in integrations, APIs, or internal logic to reference the status without relying on the name. This allows for more flexibility if the name needs to be changed for user-facing purposes while maintaining a consistent reference in the backend.
    /// </summary>
    /// <value></value>
    public string? Code { get; set; }

    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    
}