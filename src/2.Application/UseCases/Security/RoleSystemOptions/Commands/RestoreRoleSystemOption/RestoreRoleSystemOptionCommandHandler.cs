using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles <see cref="RestoreRoleSystemOptionCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the role permission's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreRoleSystemOptionCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    RolePermissionCacheInvalidator cacheInvalidator)
    : IRequestHandler<RestoreRoleSystemOptionCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        (Guid RoleId, Guid CompanyId)? restored = null;
        var response = await restorer.RestoreAsync<RoleSystemOption>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<ApplicationRole>(entity.RoleId)
                || await restorer.IsParentDeletedAsync<SystemOption>(entity.SystemOptionId)
                || await restorer.IsParentDeletedAsync<Company>(entity.CompanyId),
            beforeRestore: (entity, _) =>
            {
                restored = (entity.RoleId, entity.CompanyId);
                return Task.CompletedTask;
            },
            cancellationToken: cancellationToken);

        // SPEC 41 (Etapa 3): the restored grant changes the effective permissions of every user holding
        // the role in that company — drop their cached snapshots.
        if (response.IsSuccess && restored is { } key)
        {
            await cacheInvalidator.InvalidateAsync(key.RoleId, key.CompanyId, cancellationToken);
        }

        return response;
    }
}
