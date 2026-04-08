using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Queries;

/// <summary>
/// Query used to retrieve a single tenant-scoped project by its identifier.
/// </summary>
/// <param name="Id">The unique identifier of the requested project.</param>
/// <param name="CompanyId">The tenant identifier used to validate and scope the lookup.</param>
public sealed record GetProjectByIdQuery(Guid Id, Guid CompanyId)
    : IRequest<Response<ProjectDto>>;