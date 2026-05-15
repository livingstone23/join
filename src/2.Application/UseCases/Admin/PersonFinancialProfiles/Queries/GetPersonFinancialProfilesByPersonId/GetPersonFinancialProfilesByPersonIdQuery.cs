using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Queries;

/// <summary>
/// Query that retrieves all financial profiles for a specific person in the current tenant.
/// </summary>
/// <param name="PersonId">The unique identifier of the person.</param>
public sealed record GetPersonFinancialProfilesByPersonIdQuery(Guid PersonId) : IRequest<Response<List<PersonFinancialProfileResponseDto>>>;
