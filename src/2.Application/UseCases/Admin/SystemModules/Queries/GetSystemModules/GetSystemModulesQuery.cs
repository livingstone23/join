using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Queries;

/// <summary>
/// Query used to retrieve a paginated and filterable list of system modules.
/// </summary>
/// <param name="PageNumber">The optional page number to retrieve. When omitted, the configured default value is used.</param>
/// <param name="PageSize">The optional page size to retrieve. When omitted, the configured default value is used.</param>
/// <param name="Name">Optional inclusive partial-match filter applied to the module name.</param>
/// <param name="IsActive">Optional exact-match filter applied to the active flag.</param>
public sealed record GetSystemModulesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<SystemModuleDto>>>;