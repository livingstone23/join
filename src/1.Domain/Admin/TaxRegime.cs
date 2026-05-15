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

    /// <summary>
    /// Optional description of the tax regime.
    /// </summary>
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;


    // --- Domain Behavior ---

    public static TaxRegime Create(Guid companyId, string code, string name, string? description = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        return new TaxRegime
        {
            CompanyId = companyId,
            Code = code.Trim(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    public void Update(string code, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

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
