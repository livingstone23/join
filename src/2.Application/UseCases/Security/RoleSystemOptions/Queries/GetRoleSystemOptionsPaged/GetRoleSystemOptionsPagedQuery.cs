using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Query used to retrieve tenant-scoped paged role-system-option permission rules.
/// </summary>
public sealed record GetRoleSystemOptionsPagedQuery(
    int? PageNumber,
    int? PageSize,
    Guid? RoleId,
    Guid? SystemOptionId,
    string? RoleName,
    string? SystemOptionName,
    string? CompanyName,
    bool? CanRead,
    bool? CanCreate,
    bool? CanUpdate,
    bool? CanDelete)
    : IRequest<Response<PagedResult<RoleSystemOptionListItemDto>>>;
