using JOIN.Domain.Audit;



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

    // --- Navigation Properties ---
    public virtual ICollection<CompanyModule> CompanyModules { get; set; } = new List<CompanyModule>();
    public virtual ICollection<SystemOption> SystemOptions { get; set; } = new List<SystemOption>();
}
