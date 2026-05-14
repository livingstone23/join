using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonContacts.Queries;



/// <summary>
/// Query that retrieves a single person contact by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the person contact.</param>
public sealed record GetPersonContactByIdQuery(Guid Id) : IRequest<Response<PersonContactResponseDto>>;
