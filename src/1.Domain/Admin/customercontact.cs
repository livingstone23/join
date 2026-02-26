using JOIN.Domain.Admin;
using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a specific contact method for a Customer.
/// Links a customer with different communication channels like Email, Phone, etc.
/// </summary>
public class CustomerContact : BaseAuditableEntity
{


    /// <summary>
    /// Gets or sets the unique identifier for the associated Customer.
    /// This is the foreign key that establishes the relationship with the CRM module.
    /// </summary>
    public Guid CustomerId { get; set; }


    /// <summary>
    /// Gets or sets the category of the contact.
    /// Common values include: 'Mobile', 'Email', 'WhatsApp', 'Landline', 'Emergency'.
    /// </summary>
    /// <example>WhatsApp</example>
    public string ContactType { get; set; } = string.Empty;


    /// <summary>
    /// Gets or sets the actual contact information.
    /// Should follow international formatting for phone numbers (e.g., +34600000000) 
    /// or standard email RFC formats.
    /// </summary>
    public string ContactValue { get; set; } = string.Empty;


    /// <summary>
    /// Indicates if this is the primary contact method for the customer.
    /// </summary>
    public bool IsPrimary { get; set; }


    /// <summary>
    /// Gets or sets optional administrative notes or specific instructions regarding this contact.
    /// </summary>
    /// <value>E.g., "Do not call during office hours" or "Primary email for billing".</value>
    public string? Comments { get; set; }

    
    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Customer entity that owns this contact record.
    /// Managed through lazy loading or explicit inclusion depending on the Persistence configuration.
    /// </summary>
    public virtual Customer Customer { get; set; } = null!;

}