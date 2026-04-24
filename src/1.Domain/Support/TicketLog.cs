using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;



namespace JOIN.Domain.Support;



/// <summary>
/// This Entity represents the audit log for a ticket, 
/// capturing all significant events and changes related to the ticket's lifecycle.
/// </summary> 
public class TicketLog: BaseTenantEntity
{
    
    /// <summary>
    /// Foreign key to the associated Ticket. 
    /// </summary>
    public Guid TicketId { get; set; }

    /// <summary>
    /// Defines the nature of the log entry, such as a status change, internal note, external note, or reassignment.
    /// </summary>
    /// <value></value>
    public LogType LogType { get; set; }
    
    /// <summary>
    /// Action describing by the user indicated in the log (e.g., "Status changed from Open to In Progress", "Added note: 'Customer called for update'").
    /// it can be a description of any significant event or change related to the ticket.
    /// </summary>
    public string Summary { get; set; } = string.Empty;


    /// <summary>
    /// User who performed the action being logged. This allows tracking of who made changes or updates to the ticket, which is crucial for accountability and auditing purposes.
     /// </summary>
    public Guid UserRegisterLogId { get; set; }


    /// <summary>
    /// Indicate the previous status of the ticket.
    /// But if is the primer log, the previous status is null, because the ticket is created with the current status, and the log is registered with the same status of the ticket.
     /// This allows tracking of the ticket's status changes over time,
    /// </summary>
    /// <value></value>
    public Guid? PreviousStatusId { get; set; }

    /// <summary>
    /// Status assigned to the ticket at the moment of the log entry. 
    /// This allows tracking of the ticket's status changes over time, 
    /// which is essential for understanding the ticket's lifecycle and for reporting purposes.
    /// when register any new status in the log, the current status of the ticket is assigned to the ticket.
     /// </summary>
    /// <value></value>
    public Guid TicketStatusId { get; set; }

    /// <summary>
    /// Time unit associated with the log entry. This allows tracking of the time spent on the ticket in a specific unit (e.g., hours, minutes).
    /// </summary>
    /// <value></value>
    public Guid? TimeUnitId { get; set; }

    public decimal? ConsumedTime { get; set; }


    /// <summary>
    /// Indicates when the ticket can only see for the user who created the log and the user assigned to the ticket.
    /// This is useful for internal notes or sensitive information that should not be visible to all users for the company.
    /// </summary>
    /// <value></value>
    public bool IsOnlyForCreatedAndAssigned { get; set; } = true; 


    /// <summary>
    /// If log is for a reassignment, this field indicates the new user assigned to the ticket. This allows tracking of ticket reassignments and understanding who is currently responsible for the ticket.
    /// </summary>
    /// <value></value>
    public Guid? NewAssignedToUserId { get; set; }


    public virtual Ticket? Ticket { get; set; } = null!;

    public virtual ApplicationUser UserRegistered { get; set; } = null!;

    public virtual ApplicationUser? NewAssignedToUser { get; set; }

    public virtual TicketStatus Status { get; set; } = null!;

    public virtual TicketStatus? PreviousStatus { get; set; } = null!;

    public virtual TimeUnit? TimeUnit { get; set; } = null!;

}
