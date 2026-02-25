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
public class Customer : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the unique identifier of the Company that owns this customer.
    /// Essential for the Global Query Filter (Multi-tenancy).
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// Categorizes the customer as Physical (Natural Person) or Legal (Company/Organization).
    /// Used to apply different validation rules and tax logic.
    /// </summary>
    public PersonType PersonType { get; set; }

    /// <summary>
    /// Gets or sets the first name of the customer.
    /// Mandatory for Physical persons.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the middle name of the customer.
    /// </summary>
    public string? MiddleName { get; set; }

    /// <summary>
    /// Gets or sets the first surname of the customer.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the second surname of the customer.
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

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the owner Company.
    /// </summary>
    public virtual Company Company { get; set; } = null!;

    /// <summary>
    /// Reference to the specific Identification Type catalog entry.
    /// </summary>
    public virtual IdentificationType IdentificationType { get; set; } = null!;

    /// <summary>
    /// Collection of physical or billing addresses associated with the customer.
    /// </summary>
    public virtual ICollection<CustomerAddress> Addresses { get; set; } = new List<CustomerAddress>();

    /// <summary>
    /// Collection of communication channels (Email, Phone, WhatsApp) linked to the customer.
    /// </summary>
    public virtual ICollection<CustomerContact> Contacts { get; set; } = new List<CustomerContact>();
    
    /// <summary>
    /// Collection of tickets opened by or for this customer.
    /// </summary>
    public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}