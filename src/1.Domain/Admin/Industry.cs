using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a tenant-specific catalog of economic sectors or industries.
/// Examples: "Technology", "Healthcare", "Retail".
/// </summary>
public class Industry : BaseTenantEntity
{

    /// <summary>
    /// Gets or sets the standard or internal code for the industry.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the industry.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Optional description providing more details about what this industry covers.
    /// </summary>
    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;


    // --- Domain Behavior ---

    /// <summary>
    /// Creates a new tenant-scoped industry catalog entry.
    /// </summary>
    public static Industry Create(Guid companyId, string code, string name, string? description)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

        return new Industry
        {
            CompanyId = companyId,
            Code = code.Trim(),
            Name = name.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Updates the industry catalog data.
    /// </summary>
    public void Update(string code, string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is required.", nameof(name));
        }

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
