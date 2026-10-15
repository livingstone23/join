using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Changes the customer's stage (SPEC 55). The active state is changed only by activate/deactivate;
/// an <c>isActive</c> in the body is ignored.
/// </summary>
public sealed record UpdateCustomerCommand(
    Guid Id,
    Guid CustomerStageId) : ITransactionalCommand<Response<CustomerResponseDto>>;
