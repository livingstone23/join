using JOIN.Domain.Audit;
using JOIN.Domain.Common;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a physical location associated with a Person.
/// Optimized for international standards, supporting a full geographical hierarchy:
/// Country -> Region -> Province -> Municipality.
/// </summary>
public class PersonAddress : BaseTenantEntity
{

    
    /// <summary>
    /// Foreign key for the Person who owns this address.
    /// </summary>
    public Guid PersonId { get; set; }

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
    /// Foreign key to the Region catalog.
    /// </summary>
    public Guid? RegionId { get; set; }

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
    public bool IsDefault { get; private set; }

    /// <summary>
    /// Indicates whether the address is currently active in the system.
    /// Defaults to true. Used together with soft-delete (<see cref="BaseAuditableEntity.GcRecord"/>).
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets whether the address is eligible to be marked as default (active and not soft-deleted).
    /// </summary>
    public bool CanBeDefault => IsActive && GcRecord == ActiveGcRecord;



    // --- Navigation Properties ---

    /// <summary> Navigation to owner customer. </summary>
    public virtual Person Person { get; set; } = null!;

    /// <summary> Navigation to Country catalog. </summary>
    public virtual Country Country { get; set; } = null!;

    /// <summary> Navigation to Region catalog. </summary>
    public virtual Region? Region { get; set; } = null!;

    /// <summary> Navigation to Province/State catalog. </summary>
    public virtual Province Province { get; set; } = null!;

    /// <summary> Navigation to Municipality/City catalog. </summary>
    public virtual Municipality Municipality { get; set; } = null!;

    /// <summary> Navigation to StreetType catalog. </summary>
    public virtual StreetType StreetType { get; set; } = null!;


    // --- Domain Behavior ---

    /// <summary>
    /// Marks this address as the default for its owner.
    /// Only active, non-deleted addresses can be set as default.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the address is not active.</exception>
    public void SetAsDefault()
    {
        if (!CanBeDefault)
        {
            throw new InvalidOperationException("Only an active address can be marked as default.");
        }

        IsDefault = true;
    }

    /// <summary>
    /// Clears the default flag for this address.
    /// </summary>
    public void RemoveDefault()
    {
        if (!IsDefault)
        {
            return;
        }

        IsDefault = false;
    }

    /// <summary>
    /// Deactivates the address and clears its default flag.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RemoveDefault();
    }

    /// <summary>
    /// Reactivates the address in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
    }

}