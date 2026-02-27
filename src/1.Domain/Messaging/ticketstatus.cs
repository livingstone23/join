


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for ticket lifecycle states (e.g., Draft, Assigned, Resolved).
/// Allows dynamic workflow management and multi-language support.
/// </summary>
public class TicketStatus : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    public string? Code { get; set; }

    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    
}