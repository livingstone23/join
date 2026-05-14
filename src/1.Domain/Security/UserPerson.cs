using JOIN.Domain.Admin;
using JOIN.Domain.Audit;



namespace JOIN.Domain.Security;



/// <summary>
/// Links an identity User account to a CRM Person record.
/// Used for B2C/B2B portals where the external customer logs into the system.
/// </summary>
public class UserPerson : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser (Authentication record).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Foreign key to the Person (Business record).
    /// </summary>
    public Guid PersonId { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Person Person { get; set; } = null!;
}
