using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Changes the customer's stage (SPEC 55). The active state is changed only by activate/deactivate;
/// an <c>isActive</c> in the body is ignored. A real change is logged in the status history with the optional
/// <paramref name="Notes"/> (SPEC 66).
/// </summary>
public sealed record UpdateCustomerCommand(
    Guid Id,
    Guid CustomerStageId,
    string? Notes = null) : ITransactionalCommand<Response<CustomerResponseDto>>;
