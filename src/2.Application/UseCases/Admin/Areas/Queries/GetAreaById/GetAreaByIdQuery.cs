using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Queries;

/// <summary>
/// Query used to retrieve a single area by its identifier within a tenant scope.
/// </summary>
/// <param name="AreaId">The unique identifier of the requested area.</param>
/// <param name="CompanyId">The tenant identifier used to scope the result.</param>
public sealed record GetAreaByIdQuery(Guid AreaId, Guid CompanyId)
    : IRequest<Response<AreaDto>>;
