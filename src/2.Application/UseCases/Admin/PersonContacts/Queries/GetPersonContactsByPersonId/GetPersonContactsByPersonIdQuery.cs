using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Queries;



/// <summary>
/// Query that retrieves all active contacts for a given person in the current tenant.
/// </summary>
/// <param name="PersonId">The unique identifier of the person.</param>
public sealed record GetPersonContactsByPersonIdQuery(Guid PersonId) : IRequest<Response<List<PersonContactResponseDto>>>;
