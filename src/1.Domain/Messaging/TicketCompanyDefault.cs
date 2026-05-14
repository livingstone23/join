using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;




namespace JOIN.Domain.Messaging;



/// <summary>
/// This class lets us define the default values for the ticket when is created, 
/// for example, the default status, complexity, etc.
/// </summary>
public class TicketCompanyDefault: BaseTenantEntity
{
    
    /// <summary>
    /// Indicate the prefix to be used in the ticket code generation (e.g., "TICKET" for codes like TICKET_001).
    /// </summary>
    public string StartCode { get; set; } = "TICKET";

    /// <summary>
    /// Indicate the number of digits to be used in the ticket code generation (e.g., 3 for codes like TICKET_001).
    /// </summary>
    public int CodeSequenceLength { get; set; } = 6;
    
    /// <summary>
    /// Indicate whether to use personalized code generation based on the StartCode and CodeSequenceLength properties, 
    /// or to use the standard system format (TICK-YYYYMM-XXXX).
    /// </summary>
    public bool UsePersonalizedCode { get; set; } = false;

    /// <summary>
    /// Default status to be assigned to new tickets if not specified (e.g., "Open" status).
    /// </summary>
    public Guid? TicketStatusDefaultId { get; set; }

    /// <summary>
    /// Default complexity level to be assigned to new tickets if not specified (e.g., "Medium" complexity).
    /// </summary>
    public Guid? TicketComplexityDefaultId { get; set; }

    /// <summary>
    /// Time unit to be used for time tracking in tickets (e.g., "Hours", "Days"). This can be a reference to a time unit entity that defines the measurement unit for estimated and consumed time.
    /// </summary>
    public Guid? TimeUnitDefaultId { get; set; }
    
    /// <summary>
    /// A default area to be assigned to new tickets if not specified (e.g., "General Support" area).
    /// </summary>
    public Guid? AreaDefaultId { get; set; }

    /// <summary>
    /// A default project to be assigned to new tickets if not specified (e.g., "Default Project").
    /// </summary>
    public Guid? ProjectDefaultId { get; set; }

    /// <summary>
    /// A default communication channel to be assigned to new tickets if not specified (e.g., "Email" channel).
    /// </summary>
    public Guid? ChannelDefaultId { get; set; }


    /// <summary>
    /// Maximum number of days a ticket can remain inactive before triggering an alert or escalation.   
    /// </summary>
    /// <value></value>
    public int? MaxDayTicketInactivity { get; set; } 




    // --- Navigation ---
    public virtual TicketStatus? TicketStatusDefault { get; set; }

    public virtual TicketComplexity? TicketComplexityDefault { get; set; }
    
    public virtual TimeUnit? TimeUnitDefault { get; set; }

    public virtual Area? AreaDefault { get; set; }

    public virtual Project? ProjectDefault { get; set; }

    public virtual CommunicationChannel? ChannelDefault { get; set; }

}
