using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Queries.GetUserEffectivePermissions;

/// <summary>
/// Resolves the effective permissions of a single user inside the caller's tenant.
/// SPEC 28 / F5, item 19. The tenant comes from the JWT (SPEC 23) — the route
/// deliberately does NOT expose a <c>?companyId=</c> parameter. The result is a
/// fresh DB read (no <c>IPermissionService</c> cache) so the panel reflects the
/// current state of <c>UserRoleCompanies</c> immediately after an edit.
/// </summary>
/// <param name="UserId">User whose effective permissions are being inspected.</param>
public sealed record GetUserEffectivePermissionsQuery(Guid UserId)
    : IRequest<Response<UserEffectivePermissionsDto>>;