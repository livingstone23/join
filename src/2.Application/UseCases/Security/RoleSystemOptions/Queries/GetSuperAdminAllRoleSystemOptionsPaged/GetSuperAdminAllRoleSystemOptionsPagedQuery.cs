using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Query used by SuperAdmin to retrieve paged RoleSystemOption rules across all companies.
/// Filter shape matches <see cref="GetRoleSystemOptionsPagedQuery"/> with an optional <c>CompanyId</c> to narrow one tenant.
/// </summary>
public sealed record GetSuperAdminAllRoleSystemOptionsPagedQuery(
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
    bool? CanDelete,
    Guid? CompanyId)
    : IRequest<Response<PagedResult<RoleSystemOptionListItemDto>>>;
