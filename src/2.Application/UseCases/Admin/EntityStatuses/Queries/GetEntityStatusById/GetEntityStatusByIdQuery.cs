using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Queries;

/// <summary>
/// Query used to retrieve a single entity status by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested entity status.</param>
/// <param name="CompanyId">The tenant identifier used to validate the request scope.</param>
public sealed record GetEntityStatusByIdQuery(Guid Id, Guid CompanyId)
    : IRequest<Response<EntityStatusDto>>;
