


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for ticket effort or impact levels (e.g., Low, Medium, High, Critical).
/// Incorporates SLA (Service Level Agreement) metrics to automatically calculate deadlines.
/// </summary>
public class TicketComplexity : BaseAuditableEntity
{
    
    public string Name { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    /// <summary>
    /// Numeric weight of the complexity (e.g., 1 for Low, 4 for Critical).
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// The numeric value representing the allocated time to resolve this ticket.
    /// </summary>
    public int ResolutionTimeUnits { get; set; }

    /// <summary>
    /// Foreign key to the TimeUnit catalog (e.g., Hours, Days) used alongside ResolutionTimeUnits.
    /// </summary>
    public Guid TimeUnitId { get; set; }

    // --- Navigation Properties ---
    public virtual TimeUnit TimeUnit { get; set; } = null!;
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

}