


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for ticket complexity levels used across the messaging module.
/// </summary>
public class TicketComplexity : BaseTenantEntity
{
    /// <summary>
    /// Gets or sets the display name of the ticket complexity.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of the ticket complexity.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the numeric catalog code associated with the ticket complexity.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// Gets or sets the amount of configured time units required to resolve a ticket with this complexity.
    /// </summary>
    public int ResolutionTimeUnits { get; set; }

    /// <summary>
    /// Gets or sets the time unit associated with the configured resolution time.
    /// </summary>
    public Guid TimeUnitId { get; set; }

    /// <summary>
    /// Gets or sets whether the ticket complexity is active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Navigation to the associated time unit.
    /// </summary>
    public virtual TimeUnit TimeUnit { get; set; } = null!;

    /// <summary>
    /// Navigation to the tickets associated with this complexity.
    /// </summary>
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

}