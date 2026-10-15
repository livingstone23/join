using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Customers;

/// <summary>
/// One status event of a <see cref="Customers.Customer"/> (SPEC 55): creation, activation, confirmation or
/// deactivation, with the event that caused it. Insert-only.
/// </summary>
public class CustomerStatusHistory : BaseTenantEntity
{
    public Guid CustomerId { get; private set; }

    public CustomerStatusAction Action { get; private set; }

    public CustomerStatusSource Source { get; private set; }

    /// <summary>Id of the record that caused the event (e.g. the deactivated person), when there is one.</summary>
    public Guid? SourceReferenceId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>User who made the change.</summary>
    public Guid UserId { get; private set; }

    /// <summary>When the event happened (UTC).</summary>
    public DateTime OccurredAt { get; private set; }

    public virtual Customer Customer { get; set; } = null!;

    public virtual ApplicationUser User { get; set; } = null!;

    public static CustomerStatusHistory Create(
        Guid companyId,
        Guid customerId,
        CustomerStatusAction action,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId = null,
        string? notes = null,
        DateTime? occurredAtUtc = null)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(companyId));
        if (customerId == Guid.Empty)
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User id is required.", nameof(userId));

        return new CustomerStatusHistory
        {
            CompanyId = companyId,
            CustomerId = customerId,
            Action = action,
            Source = source,
            SourceReferenceId = sourceReferenceId,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            UserId = userId,
            OccurredAt = occurredAtUtc ?? DateTime.UtcNow
        };
    }
}
