using JOIN.Application.Common;
using JOIN.Domain.Customers;

namespace JOIN.Application.Services.Customers;

/// <summary>
/// The only way to create, activate, confirm or deactivate a customer (SPEC 55 part B). Idempotent: a person is a
/// customer at most once per company, so events never create a second row. Runs inside the calling command's
/// transaction (<see cref="ITransactionalCommand{TResponse}"/>) and leaves the changes tracked: the caller saves.
/// </summary>
public interface ICustomerActivationService
{
    /// <summary>
    /// Makes sure the person is an active customer: creates it (with <paramref name="customerStageId"/> or the
    /// company's default stage), reactivates it, or confirms it, and logs the event.
    /// Fails with <c>INVALID_PERSON</c> when the person does not exist, is inactive or deleted in the company, and
    /// with <c>INVALID_CUSTOMER_STAGE</c> when the stage is not a live, active stage of the company.
    /// </summary>
    Task<Response<CustomerActivationResult>> EnsureActiveAsync(
        Guid companyId,
        Guid personId,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId = null,
        string? notes = null,
        Guid? customerStageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the customer and logs <c>Deactivated</c>. When it was already inactive nothing changes and no
    /// history is written (<see cref="CustomerActivationResult.Action"/> is <c>null</c>).
    /// Fails with <c>CUSTOMER_NOT_FOUND</c> when the customer is not in the company.
    /// </summary>
    Task<Response<CustomerActivationResult>> DeactivateAsync(
        Guid companyId,
        Guid customerId,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId = null,
        string? notes = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The customer after an activation call and what happened to it; <see cref="Action"/> is <c>null</c> when nothing changed.
/// </summary>
public sealed record CustomerActivationResult(Customer Customer, CustomerStatusAction? Action);
