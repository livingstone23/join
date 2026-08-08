using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetRolesDetailed;

/// <summary>
/// Query for the paged, filterable list of ApplicationRole rows.
/// </summary>
/// <param name="Name">Optional case-preserving substring filter (LIKE %name%).</param>
/// <param name="IsActive">Optional active flag. null returns all rows; true returns GcRecord = 0; false returns GcRecord &lt;&gt; 0.</param>
/// <param name="Page">1-based page number; values &lt; 1 are clamped to 1.</param>
/// <param name="PageSize">Page size; values are clamped to [1, 100].</param>
public sealed record GetRolesDetailedQuery(
    string? Name,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IRequest<Response<PagedResult<RoleDto>>>;
