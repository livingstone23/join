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
    /// Gets the unique identifier for the associated Person.
    /// </summary>
    public Guid PersonId { get; private set; }

    /// <summary>
    /// Gets the category of the contact.
    /// Common values include: 'Mobile', 'Email', 'WhatsApp', 'Landline', 'Emergency'.
    /// </summary>
    public ContactType ContactType { get; private set; }

    /// <summary>
    /// Gets the actual contact information.
    /// Should follow international formatting for phone numbers (e.g., +34600000000)
    /// or standard email RFC formats.
    /// </summary>
    public string ContactValue { get; private set; } = string.Empty;

    /// <summary>
    /// Indicates if this is the primary contact method for the customer within its <see cref="ContactType"/>.
    /// </summary>
    public bool IsPrimary { get; private set; }

    /// <summary>
    /// Gets optional administrative notes or specific instructions regarding this contact.
    /// </summary>
    public string? Comments { get; private set; }

    /// <summary>
    /// Indicates whether the contact is currently active in the system.
    /// Defaults to true. Used together with soft-delete (<see cref="BaseAuditableEntity.GcRecord"/>).
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets whether the contact is eligible to be marked as primary (active and not soft-deleted).
    /// </summary>
    public bool CanBePrimary => IsActive && GcRecord == ActiveGcRecord;

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Person entity that owns this contact record.
    /// </summary>
    public virtual Person Person { get; set; } = null!;

    // --- Factory & Mutation ---

    /// <summary>
    /// Creates a new active contact for a person within a tenant.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="contactType">The contact category.</param>
    /// <param name="contactValue">The contact value (email, phone, etc.).</param>
    /// <param name="comments">Optional administrative notes.</param>
    /// <returns>A new <see cref="PersonContact"/> instance with <see cref="IsPrimary"/> set to <c>false</c>.</returns>
    public static PersonContact Create(
        Guid companyId,
        Guid personId,
        ContactType contactType,
        string contactValue,
        string? comments = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (personId == Guid.Empty)
        {
            throw new ArgumentException("PersonId is required.", nameof(personId));
        }

        if (string.IsNullOrWhiteSpace(contactValue))
        {
            throw new ArgumentException("ContactValue is required.", nameof(contactValue));
        }

        return new PersonContact
        {
            CompanyId = companyId,
            PersonId = personId,
            ContactType = contactType,
            ContactValue = contactValue.Trim(),
            Comments = comments?.Trim(),
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Updates the editable contact data.
    /// </summary>
    /// <param name="contactType">The contact category.</param>
    /// <param name="contactValue">The contact value.</param>
    /// <param name="comments">Optional administrative notes.</param>
    public void Update(ContactType contactType, string contactValue, string? comments)
    {
        if (string.IsNullOrWhiteSpace(contactValue))
        {
            throw new ArgumentException("ContactValue is required.", nameof(contactValue));
        }

        ContactType = contactType;
        ContactValue = contactValue.Trim();
        Comments = comments?.Trim();
    }

    // --- Domain Behavior ---

    /// <summary>
    /// Marks this contact as the primary for its <see cref="ContactType"/>.
    /// Only active, non-deleted contacts can be set as primary.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the contact is not active.</exception>
    public void SetAsPrimary()
    {
        if (!CanBePrimary)
        {
            throw new InvalidOperationException("Only an active contact can be marked as primary.");
        }

        IsPrimary = true;
    }

    /// <summary>
    /// Clears the primary flag for this contact.
    /// </summary>
    public void RemovePrimary()
    {
        if (!IsPrimary)
        {
            return;
        }

        IsPrimary = false;
    }

    /// <summary>
    /// Deactivates the contact and clears its primary flag.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RemovePrimary();
    }

    /// <summary>
    /// Reactivates the contact in the system.
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
