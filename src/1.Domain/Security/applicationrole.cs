using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Domain.Security;



/// <summary>
/// Custom Identity Role for the application.
/// Allows adding specific auditing or business properties to roles.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>, IAuditableEntity
{
    
    /// <summary>
    /// Detailed description of the role's permissions or purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Indicates if this is a system-default role that cannot be deleted.
    /// </summary>
    public bool IsSystemDefault { get; set; } = false;



    // --- Audit Properties (from Interface) ---
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
    public int GcRecord { get; set; } = 0;


    
    // --- Navigation Properties ---
    public virtual ICollection<UserRoleCompany> UserRoleCompanies { get; set; } = new List<UserRoleCompany>();


    /// <summary>
    /// Collection of granular permissions assigned to this role across different system options.
    /// Scoped by company due to RoleSystemOption being a BaseTenantEntity.
    /// </summary>
    public virtual ICollection<RoleSystemOption> RoleSystemOptions { get; set; } = new List<RoleSystemOption>();
    
}
