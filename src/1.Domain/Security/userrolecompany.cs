using JOIN.Domain.Audit;
using JOIN.Domain.Common;



namespace JOIN.Domain.Security;



/// <summary>
/// Defines which Role a User has within a specific Company.
/// Allows a user to be an 'Admin' in Company A, but a 'Reader' in Company B.
/// </summary>
public class UserRoleCompany : BaseAuditableEntity
{
    
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Foreign key to the ApplicationRole.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Foreign key to the Company.
    /// </summary>
    public Guid CompanyId { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    public virtual ApplicationRole Role { get; set; } = null!;
    public virtual Company Company { get; set; } = null!;

}
