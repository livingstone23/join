using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Queries;

/// <summary>
/// Query that retrieves a person business profile by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the person business profile record.</param>
public sealed record GetPersonBusinessProfileByIdQuery(Guid Id) : IRequest<Response<PersonBusinessProfileResponseDto>>;
