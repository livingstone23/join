using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Command to soft-delete a customer.
/// </summary>
public sealed record DeleteCustomerCommand(Guid Id) : IRequest<Response<Guid>>;
