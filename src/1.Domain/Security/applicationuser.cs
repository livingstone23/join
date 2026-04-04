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
    /// The user's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;


    /// <summary>
    /// The user's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;


    /// <summary>
    /// Identifier for the avatar (e.g., if stored in a cloud bucket or media table).
    /// </summary>
    public Guid? AvatarId { get; set; }


    /// <summary>
    /// URL to the user's profile image.
    /// </summary>
    public string? AvatarUrl { get; set; }


    /// <summary>
    /// Soft delete and login access control.
    /// </summary>
    public bool IsActive { get; set; } = true;


    // --- Security Properties for Authentication and Authorization ---
    /// <summary>
    /// Indicates whether the user has manually enabled Multi-Factor Authentication (MFA).
    /// </summary>
    public bool IsMfaEnabled { get; set; } = false;

    /// <summary>
    /// Stores the Base32 secret seed used to generate Time-based One-Time Password (TOTP) codes 
    /// for authenticator applications (e.g., Google or Microsoft Authenticator).
    /// </summary>
    public string? MfaSecretKey { get; set; }

    /// <summary>
    /// The name of the external identity provider used for authentication (e.g., "Google", "Microsoft"). 
    /// If null, the user authenticates via local credentials.
    /// </summary>
    public string? ExternalProvider { get; set; }

    /// <summary>
    /// The unique identifier (Subject ID) provided by the external identity provider. 
    /// Ensures a reliable link between the local account and the external identity.
    /// </summary>
    public string? ExternalProviderId { get; set; }

    /// <summary>
    /// Highest system-wide access level (JOIN Global Admin). 
    /// Grants permission to manage all tenants, global modules, and full technical configurations.
    /// </summary>
    public bool IsSuperAdmin { get; set; } = false;

    /// <summary>
    /// Tenant-level administrator (Company Super Admin). 
    /// Authorized to create the organization's structure, define internal roles, 
    /// and map system options to the specific company.
    /// </summary>
    public bool IsSuperAdminCompany { get; set; } = false;




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