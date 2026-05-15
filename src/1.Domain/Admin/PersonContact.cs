using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Enums;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents a specific contact method for a Person.
/// Links a customer with different communication channels like Email, Phone, etc.
/// </summary>
public class PersonContact : BaseTenantEntity
{


    /// <summary>
    /// Gets or sets the unique identifier for the associated Person.
    /// This is the foreign key that establishes the relationship with the CRM module.
    /// </summary>
    public Guid PersonId { get; set; }


    /// <summary>
    /// Gets or sets the category of the contact.
    /// Common values include: 'Mobile', 'Email', 'WhatsApp', 'Landline', 'Emergency'.
    /// </summary>
    /// <example>WhatsApp</example>
    public ContactType ContactType { get; set; }


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


    /// <summary>
    /// Indicates whether the person is currently active in the system.
    /// Defaults to true. Used for the Soft Delete pattern.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Person entity that owns this contact record.
    /// Managed through lazy loading or explicit inclusion depending on the Persistence configuration.
    /// </summary>
    public virtual Person Person { get; set; } = null!;

    /// <summary>
    /// indicates that the address is not active in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        IsPrimary = false; // Regla: Una dirección inactiva no puede ser la predeterminada
    }

    /// <summary>
    /// Indicates that the address is active in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }

}