using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Messaging;

/// <summary>
/// Links a User to a Company as a ticket-management agent, with the specific
/// capabilities that user holds within that tenant's ticket workflow.
/// </summary>
public class TicketUserCompany : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Grants authority to redistribute (reassign) any ticket within the company.
    /// Exactly the mechanism that decides who may reassign — see the class-level
    /// invariant enforced by <c>TicketUserCompanySuperAdminCoordinator</c>.
    /// </summary>
    public bool IsSuperAdminTicket { get; set; }

    /// <summary>
    /// Allows an operational user to mark a ticket as finished.
    /// </summary>
    public bool CanFinishTicket { get; set; }

    /// <summary>
    /// Marks the user as eligible to receive and manage ticket assignments.
    /// </summary>
    public bool CanResolveTicket { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser User { get; set; } = null!;
}
