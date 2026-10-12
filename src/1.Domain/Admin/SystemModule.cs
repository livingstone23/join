using JOIN.Domain.Audit;
using JOIN.Domain.Security;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a high-level module or macro-functionality within the JOIN system 
/// (e.g., "CRM", "Tickets", "Billing").
/// </summary>
public class SystemModule : BaseAuditableEntity
{
    /// <summary>
    /// The display name of the module.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A brief description of what this module does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The icon identifier or class name (e.g., "fas fa-cogs" or an SVG reference) 
    /// used to display the module in the UI.
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Indicates if the module is active globally in the system.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Defines the display sequence of the module in the CRM user interface.
    /// </summary>
    public int? Order { get; set; }

    /// <summary>
    /// Indicates whether this module is a BASE module: part of the minimum set every company must have.
    /// Base modules are the ones the seed assigns to companies in <c>Admin.CompanyModules</c>; optional
    /// modules (IsBase = false, e.g. "Calendar") are enabled per company by a SuperAdmin.
    /// <para>
    /// This flag does NOT control access by itself: what a company can see and use is always decided by
    /// its <see cref="CompanyModule"/> rows (active or not). IsBase only decides which modules are
    /// assigned automatically.
    /// </para>
    /// </summary>
    public bool IsBase { get; set; }

    // --- Navigation Properties ---
    public virtual ICollection<CompanyModule> CompanyModules { get; set; } = new List<CompanyModule>();
    public virtual ICollection<SystemOption> SystemOptions { get; set; } = new List<SystemOption>();

    
}
