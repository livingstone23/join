using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Workspaces;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Workspaces.Queries.GetMyRolesByCompany;

/// <summary>
/// Returns the caller's role assignments in the supplied tenant (company).
/// Backs <c>GET /api/v1/workspaces/{companyId}/my-roles</c>, consumed by the
/// frontend's "Mis roles" card (specs/05-mi-cuenta-misaccesos.md) and by the
/// company switcher (specs/06-mi-cuenta-empresas.md). Reuses
/// <see cref="IRoleUserSessionRepository.ListUserRolesInTenantAsync"/> — the
/// same primitive <c>GetMyPermissionsQueryHandler</c> already uses to build
/// the effective-permissions matrix's role list.
/// </summary>
/// <param name="sessionRepository">Dapper-backed role lookup for a user within a tenant.</param>
public sealed class GetMyRolesByCompanyQueryHandler(IRoleUserSessionRepository sessionRepository)
    : IRequestHandler<GetMyRolesByCompanyQuery, Response<IReadOnlyCollection<MyCompanyRoleDto>>>
{
    private readonly IRoleUserSessionRepository _sessionRepository = sessionRepository;

    /// <inheritdoc />
    public async Task<Response<IReadOnlyCollection<MyCompanyRoleDto>>> Handle(
        GetMyRolesByCompanyQuery request,
        CancellationToken cancellationToken)
    {
        if (request.UserId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<MyCompanyRoleDto>>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        if (request.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<MyCompanyRoleDto>>.Error(
                "TENANT_REQUIRED",
                ["The tenant could not be resolved from the request."]);
        }

        var roles = await _sessionRepository.ListUserRolesInTenantAsync(
            request.UserId,
            request.CompanyId,
            cancellationToken);

        var data = roles
            .Select(role => new MyCompanyRoleDto { RoleId = role.RoleId, RoleName = role.RoleName })
            .ToList();

        return new Response<IReadOnlyCollection<MyCompanyRoleDto>>
        {
            IsSuccess = true,
            Message = "OK",
            Data = data
        };
    }
}
