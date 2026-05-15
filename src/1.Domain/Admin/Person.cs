using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a legal or natural person who consumes services within the system.
/// This entity is a core part of the CRM module and is strictly tied to a Company (Tenant)
/// to ensure data isolation and security in a multi-tenant environment.
/// </summary>
public class Person : BaseTenantEntity
{


    /// <summary>
    /// Categorizes the person as Physical (Natural Person) or Legal (Company/Organization).
    /// Used to apply different validation rules and tax logic.
    /// </summary>
    public PersonType PersonType { get; set; }

    /// <summary>
    /// Gets or sets the foreign key for the gender.
    /// </summary>
    public Guid? GenderId { get; set; }

    /// <summary>
    /// Gets or sets the first name of the person.
    /// Mandatory for Physical persons.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the middle name of the person.
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Gets or sets the first surname of the person.
    /// </summary>
    public string? LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the second surname of the person.
    /// </summary>
    public string? SecondLastName { get; set; }

    /// <summary>
    /// Gets or sets the business or trade name. 
    /// Mandatory for Legal persons (PersonType.Legal).
    /// </summary>
    public string? CommercialName { get; set; }

    /// <summary>
    /// Gets or sets the foreign key for the identification document type (DNI, RUC, Passport).
    /// </summary>
    public Guid IdentificationTypeId { get; set; }

    /// <summary>
    /// Gets or sets the unique identification number (ID Card, Tax ID).
    /// </summary>
    public string IdentificationNumber { get; set; } = string.Empty;


    /// <summary>
    /// Indicates whether the person is currently active in the system.
    /// Defaults to true. Used for the Soft Delete pattern.
    /// </summary>
    public bool IsActive { get; private set; } = true;


    
    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the specific Gender catalog entry.
    /// </summary>
    public virtual Gender Gender { get; set; } = null!;

    /// <summary>
    /// Reference to the specific Identification Type catalog entry.
    /// </summary>
    public virtual IdentificationType IdentificationType { get; set; } = null!;

    /// <summary>
    /// Collection of physical or billing addresses associated with the person.
    /// </summary>
    public virtual ICollection<PersonAddress> Addresses { get; set; } = new List<PersonAddress>();

    /// <summary>
    /// Collection of communication channels (Email, Phone, WhatsApp) linked to the person.
    /// </summary>
    public virtual ICollection<PersonContact> Contacts { get; set; } = new List<PersonContact>();
    
    /// <summary>
    /// Collection of tickets opened by or for this person.
    /// </summary>
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();


    /// <summary>
    /// Collection of employment history records.
    /// </summary>
    public virtual ICollection<PersonEmployment> EmploymentHistory { get; set; } = new List<PersonEmployment>();


    /// <summary>
    /// Business or commercial profiles linked to the person.
    /// </summary>
    public virtual ICollection<PersonBusinessProfile> BusinessProfiles { get; set; } = new List<PersonBusinessProfile>();

    
    /// <summary>
    /// Historical record of declared financial profiles and income ranges.
    /// </summary>
    public virtual ICollection<PersonFinancialProfile> FinancialProfiles { get; set; } = new List<PersonFinancialProfile>();
    

    /// <summary>
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return; // Already deactivated
        
        IsActive = false;
        
        // Note: You could also register a Domain Event here if other aggregates 
        // need to react to this person being deactivated.
        // AddDomainEvent(new PersonDeactivatedEvent(this.Id));
    }

    /// <summary>
    /// Restores a previously soft-deleted person.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }


}