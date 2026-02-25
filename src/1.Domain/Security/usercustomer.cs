using JOIN.Domain.Admin;
using JOIN.Domain.Audit;


namespace JOIN.Domain.Security;



/// <summary>
/// Links an identity User account to a CRM Customer record.
/// Used for B2C/B2B portals where the external customer logs into the system.
/// </summary>
public class UserCustomer : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser (Authentication record).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Foreign key to the Customer (Business record).
    /// </summary>
    public Guid CustomerId { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Customer Customer { get; set; } = null!;
}
