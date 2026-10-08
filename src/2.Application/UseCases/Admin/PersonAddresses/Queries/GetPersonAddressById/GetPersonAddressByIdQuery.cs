using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.PersonAddresses.Queries;



/// <summary>
/// Query that retrieves a person address by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the person address.</param>
public sealed record GetPersonAddressByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<PersonAddressResponseDto>>;