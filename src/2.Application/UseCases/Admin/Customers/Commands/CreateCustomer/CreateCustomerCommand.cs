using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Enums;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Command to create a customer linking a person and application user.
/// </summary>
public sealed record CreateCustomerCommand(
    Guid PersonId,
    Guid UserId,
    PersonLifecycleStage PersonLifecycleStage) : ITransactionalCommand<Response<CustomerResponseDto>>;
