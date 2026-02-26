


using JOIN.Domain.Admin;
using JOIN.Domain.Audit;




namespace JOIN.Domain.Common;



/// <summary>
/// Catalog for Countries.
/// Used to standardize geographical data and international ISO compliance across the system.
/// </summary>
public class Country : BaseAuditableEntity
{
    /// <summary>
    /// Standard ISO name of the country.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// ISO 3166-1 alpha-2 or alpha-3 code (e.g., "ESP", "NIC").
    /// Essential for integrations with logistics and tax providers.
    /// </summary>
    public string IsoCode { get; set; } = string.Empty;

    /// <summary>
    /// Collection of provinces belonging to this country.
    /// Represents the first mandatory level of the geographical hierarchy.
    /// </summary>
    public virtual ICollection<Province> Provinces { get; set; } = new List<Province>();

    /// <summary>
    /// Optional collection of regions. 
    /// Used as a non-mandatory grouping for administrative or commercial purposes.
    /// </summary>
    public virtual ICollection<Region> Regions { get; set; } = new List<Region>();

    /// <summary>
    /// Collection of customer addresses located within this specific country.
    /// </summary>
    public virtual ICollection<CustomerAddress> CustomerAddresses { get; set; } = new List<CustomerAddress>();
}