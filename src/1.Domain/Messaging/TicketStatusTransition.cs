using JOIN.Domain.Audit;

namespace JOIN.Domain.Messaging;

/// <summary>
/// Optional per-company rule that restricts which <see cref="TicketStatus"/> a ticket
/// may move to from a given source <see cref="TicketStatus"/>. When no rules are
/// configured for a source status, all transitions from it remain allowed
/// (opt-in behavior — see SPEC 37).
/// </summary>
public class TicketStatusTransition : BaseTenantEntity
{
    /// <summary>
    /// Gets or sets the source <see cref="TicketStatus"/> of the allowed transition.
    /// </summary>
    public Guid FromStatusId { get; set; }

    /// <summary>
    /// Gets or sets the destination <see cref="TicketStatus"/> of the allowed transition.
    /// </summary>
    public Guid ToStatusId { get; set; }

    /// <summary>
    /// Navigation to the source status.
    /// </summary>
    public virtual TicketStatus FromStatus { get; set; } = null!;

    /// <summary>
    /// Navigation to the destination status.
    /// </summary>
    public virtual TicketStatus ToStatus { get; set; } = null!;
}