using JOIN.Domain.Admin;
using JOIN.Domain.Audit;



namespace JOIN.Domain.Common;



/// <summary>
/// Catalog for street types (e.g., Avenue, Street, Boulevard, Plaza).
/// Used to standardize address inputs for billing and logistics.
/// </summary>
public class StreetType : BaseAuditableEntity
{
    /// <summary>
    /// The official name of the street type (e.g., "Avenida").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Standard abbreviation (e.g., "Av.", "St.", "Blvd.").
    /// </summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>
    /// Indicates if the street type is currently active and can be used in addresses.
    /// </summary>
    public bool IsActive { get; set; } = true;
    
    // --- Navigation Properties ---
    public virtual ICollection<CustomerAddress> CustomerAddresses { get; set; } = new List<CustomerAddress>();
}