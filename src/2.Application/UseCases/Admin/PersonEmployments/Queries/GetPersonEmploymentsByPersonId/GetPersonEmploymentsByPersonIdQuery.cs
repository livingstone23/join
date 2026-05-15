using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Queries;

/// <summary>
/// Query that retrieves all employment records for a specific person in the current tenant.
/// </summary>
/// <param name="PersonId">The unique identifier of the person.</param>
public sealed record GetPersonEmploymentsByPersonIdQuery(Guid PersonId) : IRequest<Response<List<PersonEmploymentResponseDto>>>;
