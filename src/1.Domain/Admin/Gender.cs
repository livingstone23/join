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
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the localized display name of the gender (e.g., "Masculino", "Femenino").
    /// This is the value that will be rendered in the Blazor frontend dropdowns.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether this catalog option is currently available for new selections.
    /// Allows a tenant to soft-delete or deprecate legacy options without breaking 
    /// historical data linked to existing persons.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
}