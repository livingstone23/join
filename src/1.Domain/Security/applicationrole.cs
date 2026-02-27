using Microsoft.AspNetCore.Identity;



namespace JOIN.Domain.Security;



/// <summary>
/// Custom Identity Role for the application.
/// Allows adding specific auditing or business properties to roles.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    
    /// <summary>
    /// Detailed description of the role's permissions or purpose.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Indicates if this is a system-default role that cannot be deleted.
    /// </summary>
    public bool IsSystemDefault { get; set; } = false;

    // --- Navigation Properties ---
    public virtual ICollection<UserRoleCompany> UserRoleCompanies { get; set; } = new List<UserRoleCompany>();
    
}
