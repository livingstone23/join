using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Commands;

/// <summary>
/// Command that performs a soft delete for a customer contact in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the customer contact to delete.</param>
public sealed record DeleteCustomerContactCommand(Guid Id) : IRequest<Response<bool>>;
