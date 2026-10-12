// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Security;

namespace JOIN.Application.Common;

/// <summary>
/// SPEC 43: drops the cached menu and permissions (<c>sidebar:{companyId}:{userId}</c> and
/// <c>permissions:v2:{companyId}:{userId}</c>) of every user of a company, so enabling or disabling one of its
/// modules is reflected without waiting for the cache to expire. Same mechanism as
/// <see cref="RolePermissionCacheInvalidator"/>, scoped to the whole company.
/// </summary>
public sealed class CompanyPermissionCacheInvalidator(IUnitOfWork unitOfWork, IPermissionService permissionService)
{
    /// <summary>
    /// Invalidates the cache of the users that belong to <paramref name="companyId"/> (memberships and role
    /// assignments in that company).
    /// </summary>
    public async Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var members = await unitOfWork.GetRepository<UserCompany>()
            .GetAllIncludingDeletedAsync(uc => uc.GcRecord == 0 && uc.CompanyId == companyId);
        var roleAssignments = await unitOfWork.GetRepository<UserRoleCompany>()
            .GetAllIncludingDeletedAsync(urc => urc.GcRecord == 0 && urc.CompanyId == companyId);

        var userIds = members.Select(uc => uc.UserId)
            .Concat(roleAssignments.Select(urc => urc.UserId))
            .Distinct();

        foreach (var userId in userIds)
        {
            await permissionService.InvalidateUserCacheAsync(companyId, userId, cancellationToken);
        }
    }
}
