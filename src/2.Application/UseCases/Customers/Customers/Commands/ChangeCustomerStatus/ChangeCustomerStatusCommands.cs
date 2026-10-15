using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// <c>POST /Customers/{id}/activate</c> (SPEC 55): reactivates an inactive customer or confirms an active one
/// (logged as <c>Confirmed</c>). Source <c>Manual</c>.
/// </summary>
public sealed record ActivateCustomerCommand(Guid Id, string? Notes = null) : ITransactionalCommand<Response<CustomerResponseDto>>;

/// <summary>
/// <c>POST /Customers/{id}/deactivate</c> (SPEC 55): deactivates the customer; when already inactive nothing changes
/// and no history is written. Source <c>Manual</c>.
/// </summary>
public sealed record DeactivateCustomerCommand(Guid Id, string? Notes = null) : ITransactionalCommand<Response<CustomerResponseDto>>;

/// <summary>
/// Optional body of the activate/deactivate endpoints.
/// </summary>
public sealed record CustomerStatusChangeRequest(string? Notes = null);
