using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Customers;

/// <summary>
/// Represents a customer of the company: a CRM <see cref="Person"/> with a customer code and a stage of the
/// company's funnel (<see cref="CustomerStage"/>).
/// A person is a customer at most once per company (SPEC 55): the row is never duplicated nor deleted,
/// it only switches between active and inactive (<see cref="Reactivate"/> / <see cref="Deactivate"/>),
/// and every event that creates, activates, confirms or deactivates it, or changes its stage (SPEC 66), is logged in
/// <see cref="CustomerStatusHistory"/>.
/// Deactivating its <see cref="Person"/> deactivates the customer; reactivating the person does NOT reactivate it.
/// Linking a customer to a calendar activity or a pending person does NOT activate it (SPEC 55).
/// <see cref="BaseAuditableEntity.GcRecord"/> is inherited but always stays 0: a customer is never deleted.
/// The customer is not a system user: <see cref="UserId"/> only records who registered it.
/// </summary>
public class Customer : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the Person (Business record).
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// Foreign key to the <see cref="ApplicationUser"/> who registered this customer (audit of the registration).
    /// It is NOT the customer's login account: customers do not need to be system users.
    /// It does not change on reactivation; who made each later change is in the status history (SPEC 55).
    /// The seed uses the user <c>livingstone23@gmail.com</c> (SPEC 54).
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Customer code (max 10 characters).
    /// </summary>
    public string CustomerCode { get; private set; } = string.Empty;

    /// <summary>
    /// Commercial funnel stage, from the company's own catalog (SPEC 55). Independent of <see cref="IsActive"/>:
    /// activating or deactivating never changes it; it is set on creation and changed by PUT /Customers.
    /// </summary>
    public Guid CustomerStageId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public DateTime ActivatedAt { get; private set; }

    public DateTime? DeactivatedAt { get; private set; }

    public virtual Person Person { get; set; } = null!;

    /// <summary>User who registered the customer (see <see cref="UserId"/>).</summary>
    public virtual ApplicationUser User { get; set; } = null!;

    public virtual CustomerStage CustomerStage { get; set; } = null!;

    /// <summary>
    /// Creates a new active customer for the specified tenant and person, registered by <paramref name="userId"/>.
    /// </summary>
    public static Customer Create(
        Guid companyId,
        Guid personId,
        Guid userId,
        string customerCode,
        Guid customerStageId)
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

        if (customerStageId == Guid.Empty)
        {
            throw new ArgumentException("Customer stage id is required.", nameof(customerStageId));
        }

        return new Customer
        {
            CompanyId = companyId,
            PersonId = personId,
            UserId = userId,
            CustomerCode = normalizedCode,
            CustomerStageId = customerStageId,
            IsActive = true,
            ActivatedAt = DateTime.UtcNow,
            DeactivatedAt = null
        };
    }

    /// <summary>
    /// Moves the customer to another stage of the company's funnel. Does not touch <see cref="IsActive"/>.
    /// Returns the <see cref="CustomerStatusAction.StageChanged"/> history row the caller must persist in the same
    /// transaction (SPEC 66), or <c>null</c> when the stage is the current one (no change, no row).
    /// </summary>
    public CustomerStatusHistory? ChangeStage(
        Guid customerStageId,
        Guid userId,
        string? notes = null,
        CustomerStatusSource source = CustomerStatusSource.Manual)
    {
        if (customerStageId == Guid.Empty)
        {
            throw new ArgumentException("Customer stage id is required.", nameof(customerStageId));
        }

        if (customerStageId == CustomerStageId)
        {
            return null;
        }

        var history = CustomerStatusHistory.CreateStageChange(CompanyId, Id, CustomerStageId, customerStageId, userId, notes, source);
        CustomerStageId = customerStageId;
        return history;
    }

    /// <summary>
    /// Deactivates the customer record. Returns <c>false</c> when it was already inactive.
    /// </summary>
    public bool Deactivate()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        DeactivatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Reactivates the customer record. Returns <c>false</c> when it was already active.
    /// </summary>
    public bool Reactivate()
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        DeactivatedAt = null;
        ActivatedAt = DateTime.UtcNow;
        return true;
    }
}
