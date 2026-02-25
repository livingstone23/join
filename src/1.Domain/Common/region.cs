


using JOIN.Domain.Audit;




namespace JOIN.Domain.Common;



/// <summary>
/// Represents a major administrative division within a Country (e.g., Autonomous Community, State, or Region).
/// Acts as the second level in the geographical hierarchy: Country -> Region.
/// </summary>
public class Region : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the official name of the region.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the administrative or ISO code for the region. 
    /// Essential for electronic invoicing and logistics integrations.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the foreign key of the parent Country.
    /// </summary>
    public Guid CountryId { get; set; }

    // --- Navigation Properties ---

    /// <summary>
    /// Navigation property to the parent Country.
    /// </summary>
    public virtual Country Country { get; set; } = null!;

    /// <summary>
    /// Collection of provinces or secondary divisions belonging to this region.
    /// </summary>
    public virtual ICollection<Province> Provinces { get; set; } = new List<Province>();
}