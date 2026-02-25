


using JOIN.Domain.Admin;
using JOIN.Domain.Audit;



namespace JOIN.Domain.Common;



/// <summary>
/// Represents a specific Municipality, City, or Local District[cite: 784].
/// This is the lowest level of the geographical hierarchy: Province -> Municipality.
/// </summary>
public class Municipality : BaseAuditableEntity
{
    /// <summary>
    /// Official name of the municipality or local city.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Internal postal or administrative code assigned by local authorities[cite: 789].
    /// </summary>
    public string? Code { get; set; } 

    /// <summary>
    /// Foreign key of the parent Province.
    /// </summary>
    public Guid ProvinceId { get; set; }

    /// <summary>
    /// Navigation property to the parent Province.
    /// </summary>
    public virtual Province Province { get; set; } = null!;

    /// <summary>
    /// Collection of customer addresses located within this specific municipality.
    /// </summary>
    public virtual ICollection<CustomerAddress> CustomerAddresses { get; set; } = new List<CustomerAddress>();

}