using JOIN.Domain.Audit;
using JOIN.Domain.Common;



namespace JOIN.Domain.Security;



/// <summary>
/// Intersect entity that grants a User access to a specific Company (Tenant).
/// Essential for multi-tenant data isolation.
/// </summary>
public class UserCompany : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Foreign key to the Company (Tenant).
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Indicates if this is the default company loaded when the user logs in.
    /// </summary>
    public bool IsDefault { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual Company Company { get; set; } = null!;
}
