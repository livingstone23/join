using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a tenant-specific catalog of tax regimes.
/// Examples: "Régimen General", "Régimen Simplificado", "Monotributo".
/// </summary>
public class TaxRegime : BaseTenantEntity
{

    
    /// <summary>
    /// The official tax authority code (e.g., SUNAT, SAT, DIAN code).
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// The human-readable name of the tax regime.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;


    // --- Domain Behavior ---

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }

    
}
