using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Queries;

/// <summary>
/// Query that retrieves all business profiles for a specific person in the current tenant.
/// </summary>
/// <param name="PersonId">The unique identifier of the person.</param>
public sealed record GetPersonBusinessProfilesByPersonIdQuery(Guid PersonId) : IRequest<Response<List<PersonBusinessProfileResponseDto>>>;
