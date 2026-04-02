using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Command to soft-delete an existing customer aggregate.
/// </summary>
/// <param name="Id">The customer identifier to delete.</param>
public record DeleteCustomerCommand(Guid Id) : IRequest<Response<Guid>>;
