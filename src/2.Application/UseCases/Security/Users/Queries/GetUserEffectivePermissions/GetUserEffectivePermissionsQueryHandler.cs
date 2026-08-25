using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Queries.GetUserEffectivePermissions;

/// <summary>
/// Handler for <see cref="GetUserEffectivePermissionsQuery"/>. SPEC 28 / F5, item 19.
/// Tenant comes from the JWT (SPEC 23). <see cref="IUserAdminRepository.GetAdminSnapshotAsync"/>
/// is the only check that distinguishes "user does not exist" (null → USER_NOT_FOUND)
/// from "user exists with no roles in this tenant" (DTO with empty RoleIds and a full
/// grid with every Granted flag collapsed to false). The permission cache is
/// deliberately bypassed so the panel shows live state.
/// </summary>
public sealed class GetUserEffectivePermissionsQueryHandler(
    IUserAdminRepository userAdminRepository,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetUserEffectivePermissionsQuery, Response<UserEffectivePermissionsDto>>
{
    public async Task<Response<UserEffectivePermissionsDto>> Handle(
        GetUserEffectivePermissionsQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<UserEffectivePermissionsDto>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to resolve effective permissions."]);
        }

        // The snapshot returns null only when the user does not exist or is
        // soft-deleted — it does not filter on IsActive, so the panel can still
        // inspect inactive accounts.
        var snapshot = await userAdminRepository.GetAdminSnapshotAsync(
            request.UserId, companyId, cancellationToken);
        if (snapshot is null)
        {
            return Response<UserEffectivePermissionsDto>.Error(
                "USER_NOT_FOUND",
                ["User does not exist or is soft-deleted."]);
        }

        var dto = await userAdminRepository.GetEffectivePermissionsAsync(
            request.UserId, companyId, cancellationToken);
        if (dto is null)
        {
            // Race: snapshot succeeded but the matrix query found no rows. Treat as
            // USER_NOT_FOUND for consistency.
            return Response<UserEffectivePermissionsDto>.Error(
                "USER_NOT_FOUND",
                ["User does not exist or is soft-deleted."]);
        }

        return new Response<UserEffectivePermissionsDto>
        {
            IsSuccess = true,
            Message = "Effective permissions retrieved successfully.",
            Data = dto
        };
    }
}