using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Microsoft.AspNetCore.Identity;



namespace JOIN.Domain.Security;



/// <summary>
/// Custom Identity User representing system access accounts.
/// Extends base identity to include multi-tenant and audit support.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>
    /// URL to the user's profile image.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Soft delete and login access control.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // --- Audit Properties (from Interface) ---
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
    public int GcRecord { get; set; } = 0;

    // --- Navigation Properties ---
    public virtual ICollection<UserCompany> UserCompanies { get; set; } = new List<UserCompany>();
    public virtual ICollection<UserRoleCompany> UserRoleCompanies { get; set; } = new List<UserRoleCompany>();
    public virtual ICollection<UserCommunicationChannel> Channels { get; set; } = new List<UserCommunicationChannel>();
    
}