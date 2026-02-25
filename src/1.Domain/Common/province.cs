


using JOIN.Domain.Audit;
using JOIN.Domain.Common;



namespace JOIN.Domain.Common;



/// <summary>
/// Represents a secondary administrative division (e.g., Province, State, or County)[cite: 809].
/// Acts as the mandatory second level: Country -> Province.
/// </summary>
public class Province : BaseAuditableEntity
{
    /// <summary>
    /// Official name of the province or department.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Specific administrative code (e.g., ISO-3166-2).
    /// Used for regional tax identification and postal routing[cite: 813].
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key for the parent Country.
    /// </summary>
    public Guid CountryId { get; set; }

    /// <summary>
    /// Navigation property to the parent Country.
    /// </summary>
    public virtual Country Country { get; set; } = null!;

    /// <summary>
    /// Optional Foreign key for an administrative Region.
    /// Allows grouping provinces without breaking the strict geographical chain.
    /// </summary>
    public Guid? RegionId { get; set; } 

    /// <summary>
    /// Navigation property to the optional Region agrupator.
    /// </summary>
    public virtual Region? Region { get; set; }

    /// <summary>
    /// Collection of municipalities or local districts within this province.
    /// </summary>
    public virtual ICollection<Municipality> Municipalities { get; set; } = new List<Municipality>();

}