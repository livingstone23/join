using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a tenant-specific catalog for Gender options.
/// Inheriting from BaseTenantEntity ensures that each company manages its own 
/// independent list of genders, fully isolated from other tenants.
/// </summary>
public class Gender : BaseTenantEntity
{
    
    /// <summary>
    /// Gets or sets the standard or internal code for the gender (e.g., "M", "F", "NB").
    /// Essential for API integrations or data imports where string matching is fragile.
    /// </summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized display name of the gender (e.g., "Masculino", "Femenino").
    /// This is the value that will be rendered in the Blazor frontend dropdowns.
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Indicates whether this catalog option is currently available for new selections.
    /// Allows a tenant to soft-delete or deprecate legacy options without breaking 
    /// historical data linked to existing persons.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    public static Gender Create(Guid companyId, string code, string name)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        return new Gender
        {
            CompanyId = companyId,
            Code = code.Trim(),
            Name = name.Trim(),
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Creates a gender using the display name; derives a short code for known catalog values.
    /// </summary>
    public static Gender Create(Guid companyId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        var normalizedName = name.Trim();
        var code = normalizedName.ToUpperInvariant() switch
        {
            "MASCULINO" => "M",
            "FEMENINO" => "F",
            _ => normalizedName.Length >= 2
                ? normalizedName[..2].ToUpperInvariant()
                : normalizedName.ToUpperInvariant()
        };

        return Create(companyId, code, normalizedName);
    }

    public void Update(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Code = code.Trim();
        Name = name.Trim();
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