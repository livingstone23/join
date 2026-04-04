using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;



namespace JOIN.Domain.Security;



/// <summary>
/// Defines the specific permissions a Role has over a SystemOption.
/// This allows granular access control (Read, Create, Update, Delete) per screen.
/// </summary>
public class RoleSystemOption : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the ApplicationRole.
    /// </summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Foreign key to the SystemOption (Screen/Menu).
    /// </summary>
    public Guid SystemOptionId { get; set; }


    /// <summary>
    /// Foreign key to the SystemOption (Screen/Menu).
    /// </summary>
    public Guid CompanyId { get; set; }

    // --- Granular Permissions ---
    // These override or specify the exact access level the role has for this option.

    /// <summary>
    /// Indicates if the role can view or access this screen/data.
    /// </summary>
    public bool CanRead { get; set; }

    /// <summary>
    /// Indicates if the role can create new records in this screen.
    /// </summary>
    public bool CanCreate { get; set; }

    /// <summary>
    /// Indicates if the role can edit existing records in this screen.
    /// </summary>
    public bool CanUpdate { get; set; }

    /// <summary>
    /// Indicates if the role can perform soft deletes on records in this screen.
    /// </summary>
    public bool CanDelete { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationRole Role { get; set; } = null!;
    public virtual SystemOption SystemOption { get; set; } = null!;

    /// <summary>
    /// Navigation property to the Company (Tenant) that owns this permission set.
    /// Linked via the CompanyId inherited from BaseTenantEntity.
    /// </summary>
    public virtual Company Company { get; set; } = null!;
    

}