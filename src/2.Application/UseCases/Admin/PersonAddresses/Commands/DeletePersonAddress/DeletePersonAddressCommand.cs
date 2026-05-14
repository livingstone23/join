using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Command that performs a soft delete for a customer address in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the customer address to delete.</param>
public sealed record DeletePersonAddressCommand(Guid Id) : IRequest<Response<bool>>;