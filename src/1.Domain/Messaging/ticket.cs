


using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Exceptions;
using JOIN.Domain.Security;
using JOIN.Domain.Support;



namespace JOIN.Domain.Messaging;



/// <summary>
/// Represents a service request with advanced tracking, SLAs, and multi-channel support.
/// </summary>
public class Ticket : BaseTenantEntity
{

    /// <summary>
    /// Human-readable ticket identifier managed exclusively by the domain.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // --- Time & Metrics ---
    public Guid TimeUnitId { get; set; }
    public decimal EstimatedTime { get; set; } // Tiempo proyectado
    public decimal ConsumedTime { get; set; }  // Tiempo consumido
    public decimal? EffortPoints { get; set; } // Puntos de esfuerzo opcionales

    // --- Status & Complexity (Tables) ---
    public Guid TicketStatusId { get; set; }
    public Guid TicketComplexityId { get; set; }

    // --- Context & Hierarchy ---

    public Guid? CustomerId { get; set; }
    public Guid? ProjectId { get; set; } // Opcional
    public Guid? AreaId { get; set; }    // Opcional
    public Guid ChannelId { get; set; } // Canal de creación (Bot, Web, etc.)
    
    /// <summary> Link to the parent ticket if this is a follow-up. </summary>
    public Guid? PrecedentTicketId { get; set; }

    /// <summary>
    /// Indicates whether this ticket is visible to external users (e.g., customers) 
    /// through a self-service portal or external tracking system.
    /// </summary>
    public bool IsVisibleToExternals { get; set; } = false;
    
    
    // --- Ownership ---
    public Guid CreatedByUserId { get; set; } // Usuario creó
    public Guid? AssignedToUserId { get; set; } // Usuario gestiona


    // --- Navigation ---
    public virtual Customer? Customer { get; set; }
    public virtual Project? Project { get; set; }
    public virtual Area? Area { get; set; }
    public virtual CommunicationChannel Channel { get; set; } = null!;

    public virtual ApplicationUser CreatedByUser { get; set; } = null!;
    public virtual ApplicationUser? AssignedToUser { get; set; }

    public virtual TicketStatus Status { get; set; } = null!;
    public virtual TicketComplexity Complexity { get; set; } = null!;
    public virtual TimeUnit TimeUnit { get; set; } = null!;
    
    public virtual Ticket? PrecedentTicket { get; set; }
    public virtual ICollection<TicketNotification> Notifications { get; set; } = new List<TicketNotification>();

    public virtual ICollection<TicketLog> TicketLogs { get; set; } = new List<TicketLog>();



    /// <summary> Collection of follow-up tickets spawned from this specific ticket. </summary>
    public virtual ICollection<Ticket> ChildTickets { get; set; } = new List<Ticket>();

    /// <summary>
    /// Assigns the standard system-generated code using the format TICK-YYYYMM-XXXX.
    /// </summary>
    /// <param name="year">The UTC year used for the ticket prefix.</param>
    /// <param name="month">The UTC month used for the ticket prefix.</param>
    /// <param name="sequence">The monthly ticket sequence number.</param>
    public void SetStandardCode(int year, int month, int sequence)
    {
        if (year is < 1 or > 9999)
        {
            throw new DomainException("INVALID_TICKET_YEAR", "Year must be between 1 and 9999.");
        }

        if (month is < 1 or > 12)
        {
            throw new DomainException("INVALID_TICKET_MONTH", "Month must be between 1 and 12.");
        }

        if (sequence <= 0)
        {
            throw new DomainException("INVALID_TICKET_SEQUENCE", "Sequence must be greater than zero.");
        }

        Code = $"TICK-{year:D4}{month:D2}-{sequence:D4}";
    }

    /// <summary>
    /// Assigns a personalized code using the configured start code and padded numeric sequence.
    /// </summary>
    /// <param name="startCode">The configured ticket prefix for the tenant.</param>
    /// <param name="sequence">The ticket sequence number to format.</param>
    /// <param name="length">The total length of the numeric sequence padding.</param>
    public void SetPersonalizedCode(string startCode, int sequence, int length)
    {
        if (string.IsNullOrWhiteSpace(startCode))
        {
            throw new DomainException("INVALID_TICKET_PREFIX", "Start code is required.");
        }

        if (sequence <= 0)
        {
            throw new DomainException("INVALID_TICKET_SEQUENCE", "Sequence must be greater than zero.");
        }

        if (length <= 0)
        {
            throw new DomainException("INVALID_TICKET_CODE_LENGTH", "Length must be greater than zero.");
        }

        var normalizedStartCode = startCode.Trim().ToUpperInvariant();
        Code = $"{normalizedStartCode}-{sequence.ToString($"D{length}")}";
    }

    /// <summary>
    /// Appends a new audit log entry to the current ticket aggregate.
    /// </summary>
    /// <param name="userId">The identifier of the user responsible for the action.</param>
    /// <param name="logType">The type of audit event to register.</param>
    /// <param name="summary">The human-readable summary of the action.</param>
    /// <param name="previousStatusId">The previous workflow status when the log represents a status transition.</param>
    /// <param name="newAssignedToUserId">The new assignee when the log represents a reassignment.</param>
    public void AddLog(
        Guid userId,
        LogType logType,
        string summary,
        Guid? previousStatusId = null,
        Guid? newAssignedToUserId = null)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("INVALID_TICKET_LOG_USER", "User identifier is required.");
        }

        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new DomainException("INVALID_TICKET_LOG_SUMMARY", "Log summary is required.");
        }

        TicketLogs.Add(new TicketLog
        {
            TicketId = Id,
            CompanyId = CompanyId,
            LogType = logType,
            Summary = summary.Trim(),
            UserRegisterLogId = userId,
            PreviousStatusId = previousStatusId,
            TicketStatusId = TicketStatusId,
            TimeUnitId = TimeUnitId == Guid.Empty ? null : TimeUnitId,
            ConsumedTime = ConsumedTime,
            IsOnlyForCreatedAndAssigned = false,
            NewAssignedToUserId = newAssignedToUserId,
            Created = DateTime.UtcNow,
            CreatedBy = userId.ToString(),
            GcRecord = BaseAuditableEntity.ActiveGcRecord
        });
    }
}