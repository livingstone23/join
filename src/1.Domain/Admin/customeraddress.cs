using JOIN.Domain.Audit;
using JOIN.Domain.Common;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a physical location associated with a Customer.
/// Optimized for international standards, supporting a full geographical hierarchy:
/// Country -> Region -> Province -> Municipality.
/// </summary>
public class CustomerAddress : BaseAuditableEntity
{
    /// <summary>
    /// Foreign key for the Customer who owns this address.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Address Line 1: Street address, P.O. box, company name, c/o.
    /// </summary>
    public string AddressLine1 { get; set; } = string.Empty;

    /// <summary>
    /// Address Line 2: Apartment, suite, unit, building, floor, etc.
    /// </summary>
    public string? AddressLine2 { get; set; }

    /// <summary>
    /// Postcode or ZIP code. Essential for routing and logistics.
    /// </summary>
    public string ZipCode { get; set; } = string.Empty;


    /// <summary>
    /// Foreign key to the Street type catalog.
    /// </summary>
    public Guid StreetTypeId { get; set; }


    /// <summary>
    /// Foreign key to the Country catalog.
    /// </summary>
    public Guid CountryId { get; set; }

    /// <summary>
    /// Foreign key to the Province catalog (Secondary administrative division).
    /// </summary>
    public Guid ProvinceId { get; set; }

    /// <summary>
    /// Foreign key to the Municipality/City catalog (Local division).
    /// </summary>
    public Guid MunicipalityId { get; set; }

    /// <summary>
    /// Indicates if this is the default shipping address for the customer.
    /// </summary>
    public bool IsDefault { get; set; }

    // --- Navigation Properties ---

    /// <summary> Navigation to owner customer. </summary>
    public virtual Customer Customer { get; set; } = null!;

    /// <summary> Navigation to Country catalog. </summary>
    public virtual Country Country { get; set; } = null!;

    /// <summary> Navigation to Province/State catalog. </summary>
    public virtual Province Province { get; set; } = null!;

    /// <summary> Navigation to Municipality/City catalog. </summary>
    public virtual Municipality Municipality { get; set; } = null!;

    /// <summary> Navigation to StreetType catalog. </summary>
    public virtual StreetType StreetType { get; set; } = null!;
}