using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a specific screen, menu item, or action within a SystemModule.
/// Supports a hierarchical structure (Parent-Child) for nested menus.
/// </summary>
public class SystemOption : BaseAuditableEntity
{


    /// <summary>
    /// Foreign key to the parent SystemModule.
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// The display name of the option (e.g., "Manage Tickets").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The internal route or URL path for the frontend (e.g., "/tickets/manage").
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// The icon used in the UI for this specific menu item.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Foreign key to another SystemOption if this is a sub-menu. 
    /// If null, this is a root/parent menu item.
    /// </summary>
    public Guid? ParentId { get; set; }

    // --- Default Supported Actions ---
    // These indicate if the screen *supports* these actions by default.
    
    public bool CanRead { get; set; } = true;
    public bool CanCreate { get; set; } = true;
    public bool CanUpdate { get; set; } = true;
    public bool CanDelete { get; set; } = true;

    // --- Navigation Properties ---
    public virtual SystemModule Module { get; set; } = null!;
    
    /// <summary>
    /// Reference to the parent option (if any).
    /// </summary>
    public virtual SystemOption? Parent { get; set; }

    /// <summary>
    /// Collection of child sub-menus.
    /// </summary>
    public virtual ICollection<SystemOption> Children { get; set; } = new List<SystemOption>();

    /// <summary>
    /// Permissions configured for this option across different roles.
    /// </summary>
    public virtual ICollection<RoleSystemOption> RoleOptions { get; set; } = new List<RoleSystemOption>();


}
