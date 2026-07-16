using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Command to update an existing customer.
/// </summary>
public sealed record UpdateCustomerCommand(
    Guid Id,
    PersonLifecycleStage PersonLifecycleStage,
    bool IsActive) : ITransactionalCommand<Response<CustomerResponseDto>>;
