using JOIN.Application.Common;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;



/// <summary>
/// Command that performs a soft delete for a person contact in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the person contact to delete.</param>
public sealed record DeletePersonContactCommand(Guid Id) : IRequest<Response<bool>>;
