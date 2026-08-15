using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetMyPermissions;

/// <summary>
/// Reads the caller's combined permissions matrix for the JWT-resolved tenant.
/// Uses <see cref="IRoleUserSessionRepository"/> as the spec mandates; two Dapper
/// queries — one for the user's roles and one for the per-option flag grid.
/// </summary>
/// <param name="currentUserService">Resolves the calling user id and the tenant (CompanyId).</param>
/// <param name="sessionRepository">Dapper-backed matrix primitives (SPEC 26 / F4).</param>
public sealed class GetMyPermissionsQueryHandler(
    ICurrentUserService currentUserService,
    IRoleUserSessionRepository sessionRepository)
    : IRequestHandler<GetMyPermissionsQuery, Response<MyPermissionsDto>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IRoleUserSessionRepository _sessionRepository = sessionRepository;

    /// <inheritdoc />
    public async Task<Response<MyPermissionsDto>> Handle(
        GetMyPermissionsQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<MyPermissionsDto>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            // F4 acceptance: tenant must resolve from the JWT claim or X-Company-Id. Query-string
            // ?companyId= is ignored — return 400.
            return Response<MyPermissionsDto>.Error(
                "TENANT_REQUIRED",
                ["The tenant could not be resolved from the current request."]);
        }

        var userRoles = await _sessionRepository
            .ListUserRolesInTenantAsync(callerUserId, companyId, cancellationToken);

        var flagGrid = await _sessionRepository.GetPermissionFlagGridAsync(
            userRoles.Select(r => r.RoleId).ToList(),
            companyId,
            cancellationToken);

        // Compose the matrix DTO.
        var matrix = new RoleSystemOptionMatrixDto(
            RoleId: userRoles.Count == 1 ? userRoles[0].RoleId : Guid.Empty,
            RoleName: BuildRoleName(userRoles),
            Modules: BuildModules(flagGrid));

        var dto = new MyPermissionsDto
        {
            UserId = callerUserId,
            CompanyId = companyId,
            RoleIds = userRoles.Select(r => r.RoleId).ToList(),
            Matrix = matrix
        };

        return new Response<MyPermissionsDto>
        {
            IsSuccess = true,
            Message = "OK",
            Data = dto
        };
    }

    /// <summary>
    /// Renders the matrix's <see cref="RoleSystemOptionMatrixDto.RoleName"/>.
    /// Empty string when the user has no roles in the tenant; verbatim name for one role;
    /// "<first> + N más" otherwise.
    /// </summary>
    private static string BuildRoleName(IReadOnlyList<UserRoleLookupRow> userRoles)
    {
        if (userRoles.Count == 0)
        {
            return string.Empty;
        }

        if (userRoles.Count == 1)
        {
            return userRoles[0].RoleName;
        }

        var others = userRoles.Count - 1;
        return $"{userRoles[0].RoleName} + {others} más";
    }

    /// <summary>
    /// Groups the flat flag grid by module and converts each grid row into a matrix option DTO.
    /// </summary>
    private static IReadOnlyList<RoleSystemOptionMatrixModuleDto> BuildModules(
        IReadOnlyList<PermissionFlagGridRow> flagGrid)
    {
        return flagGrid
            .GroupBy(row => row.ModuleId)
            .Select(group => new RoleSystemOptionMatrixModuleDto(
                ModuleId: group.Key,
                ModuleName: group.First().ModuleName,
                Options: group
                    .OrderBy(opt => opt.DisplayOrder)
                    .ThenBy(opt => opt.OptionName)
                    .Select(opt => new RoleSystemOptionMatrixOptionDto(
                        SystemOptionId: opt.SystemOptionId,
                        Name: opt.OptionName,
                        Route: opt.OptionRoute,
                        Supports: opt.Supports,
                        Granted: opt.Granted))
                    .ToList()))
            .ToList();
    }
}
