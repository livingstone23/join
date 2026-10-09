// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Security;

namespace JOIN.Application.Common;

/// <summary>
/// Drops the permission snapshot cache (<c>permissions:v2:{companyId}:{userId}</c>) of every user holding a
/// role in a company. Used after restoring a <see cref="RoleCompany"/> or a <see cref="RoleSystemOption"/>
/// (SPEC 41, Etapa 3), so the restored permissions apply on the next request instead of after the cache TTL.
/// </summary>
public sealed class RolePermissionCacheInvalidator(IUnitOfWork unitOfWork, IPermissionService permissionService)
{
    public async Task InvalidateAsync(Guid roleId, Guid companyId, CancellationToken cancellationToken = default)
    {
        var assignments = await unitOfWork.GetRepository<UserRoleCompany>()
            .GetAllIncludingDeletedAsync(urc => urc.GcRecord == 0 && urc.RoleId == roleId && urc.CompanyId == companyId);

        foreach (var userId in assignments.Select(urc => urc.UserId).Distinct())
        {
            await permissionService.InvalidateUserCacheAsync(companyId, userId, cancellationToken);
        }
    }
}
