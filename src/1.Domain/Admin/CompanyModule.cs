using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Associative entity defining which SystemModules are enabled for a specific Company.
/// Acts as a subscription or feature-flag table for the Tenant.
/// </summary>
public class CompanyModule : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the SystemModule.
    /// (Note: CompanyId is already inherited from BaseTenantEntity).
    /// </summary>
    public Guid ModuleId { get; set; }

    /// <summary>
    /// Indicates if the module is currently active for this specific company.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // --- Navigation Properties ---
    public virtual SystemModule Module { get; set; } = null!;
}
