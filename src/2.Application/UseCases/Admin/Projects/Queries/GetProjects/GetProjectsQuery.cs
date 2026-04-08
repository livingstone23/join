using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Queries;

/// <summary>
/// Query used to retrieve a paginated and filterable project list for a specific tenant.
/// </summary>
/// <param name="CompanyId">The tenant identifier used to scope the result set.</param>
/// <param name="PageNumber">The optional page number to retrieve. When omitted, the configured default value is used.</param>
/// <param name="PageSize">The optional page size to retrieve. When omitted, the configured default value is used.</param>
/// <param name="Name">Optional inclusive partial-match filter applied to the project name.</param>
/// <param name="EntityStatusId">Optional exact-match filter applied to the linked entity status.</param>
public sealed record GetProjectsQuery(
    Guid CompanyId,
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    Guid? EntityStatusId = null)
    : IRequest<Response<PagedResult<ProjectDto>>>;