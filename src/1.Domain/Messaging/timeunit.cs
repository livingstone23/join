


using JOIN.Domain.Audit;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Catalog for time measurements (e.g., Hours, Days, Weeks).
/// Standardizes how resolution time is recorded.
/// </summary>
public class TimeUnit : BaseAuditableEntity
{


    public string Name { get; set; } = string.Empty;
    

    /// <summary>
    /// Multiplier or code for time calculation (e.g., 1 for Hour, 24 for Day).
    /// </summary>
    public int Code { get; set; }

    
    /// <summary>
    /// Indicates if the time unit is currently active and should be used in calculations. This allows for deprecating certain time units without deleting them, preserving historical data integrity while preventing their use in new tickets or complexity definitions.
    /// </summary>
    /// <value></value>
    public bool IsActive { get; set; } = true;

    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();

    public virtual ICollection<TicketComplexity> TicketComplexities { get; set; } = new List<TicketComplexity>();

}