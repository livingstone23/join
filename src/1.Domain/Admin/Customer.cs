using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Admin;

/// <summary>
/// Represents a customer in the system linking a CRM person with an identity user and lifecycle stage.
/// </summary>
public class Customer : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the Person (Business record).
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Foreign key to the ApplicationUser (Authentication record).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Customer code (max 10 characters).
    /// </summary>
    public string CustomerCode { get; private set; } = string.Empty;

    /// <summary>
    /// Person lifecycle stage.
    /// </summary>
    public PersonLifecycleStage PersonLifecycleStage { get; set; }

    public bool IsActive { get; private set; } = true;

    public DateTime ActivatedAt { get; private set; }

    public DateTime? DeactivatedAt { get; private set; }

    public virtual Person Person { get; set; } = null!;

    public virtual ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// Creates a new customer for the specified tenant, person, and user.
    /// </summary>
    public static Customer Create(
        Guid companyId,
        Guid personId,
        Guid userId,
        string customerCode,
        PersonLifecycleStage lifecycleStage)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (personId == Guid.Empty)
        {
            throw new ArgumentException("Person id is required.", nameof(personId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(customerCode))
        {
            throw new ArgumentException("Customer code is required.", nameof(customerCode));
        }

        var normalizedCode = customerCode.Trim();
        if (normalizedCode.Length > 10)
        {
            throw new ArgumentException("Customer code cannot exceed 10 characters.", nameof(customerCode));
        }

        if (!Enum.IsDefined(lifecycleStage))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleStage), lifecycleStage, "Invalid lifecycle stage.");
        }

        return new Customer
        {
            CompanyId = companyId,
            PersonId = personId,
            UserId = userId,
            CustomerCode = normalizedCode,
            PersonLifecycleStage = lifecycleStage,
            IsActive = true,
            ActivatedAt = DateTime.UtcNow,
            DeactivatedAt = null
        };
    }

    /// <summary>
    /// Updates the lifecycle stage of the customer.
    /// </summary>
    public void UpdateLifecycle(PersonLifecycleStage lifecycleStage)
    {
        if (!Enum.IsDefined(lifecycleStage))
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleStage), lifecycleStage, "Invalid lifecycle stage.");
        }

        PersonLifecycleStage = lifecycleStage;
    }

    /// <summary>
    /// Deactivates the customer record.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reactivates the customer record.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        DeactivatedAt = null;
        ActivatedAt = DateTime.UtcNow;
    }
}
