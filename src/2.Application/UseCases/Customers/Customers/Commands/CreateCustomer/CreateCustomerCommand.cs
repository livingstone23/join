using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Makes a person an active customer of the current company (SPEC 55, idempotent): creates it, reactivates it or
/// confirms it. The registering user is the authenticated one; a <c>userId</c> in the body is ignored.
/// </summary>
/// <param name="PersonId">The person.</param>
/// <param name="CustomerStageId">Stage used only on creation; without value, the company's default stage.</param>
public sealed record CreateCustomerCommand(
    Guid PersonId,
    Guid? CustomerStageId = null) : ITransactionalCommand<Response<CustomerResponseDto>>;
