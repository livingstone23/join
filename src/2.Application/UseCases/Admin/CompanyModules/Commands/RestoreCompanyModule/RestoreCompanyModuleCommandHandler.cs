using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles <see cref="RestoreCompanyModuleCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the company module's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreCompanyModuleCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    CompanyPermissionCacheInvalidator cacheInvalidator,
    IPermissionService permissionService)
    : IRequestHandler<RestoreCompanyModuleCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreCompanyModuleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        var response = await restorer.RestoreAsync<CompanyModule>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Company>(entity.CompanyId)
                || await restorer.IsParentDeletedAsync<SystemModule>(entity.ModuleId),
            cancellationToken: cancellationToken);

        if (response.IsSuccess)
        {
            var tenantId = TenantResolver.Resolve(currentUserService, request.CompanyId);

            // SPEC 43: the restored module shows up in the company's menu right away.
            await cacheInvalidator.InvalidateAsync(tenantId, cancellationToken);

            // SPEC 48 E: the per-company module snapshot must also rebuild so the API gate reflects the change.
            await permissionService.InvalidateCompanyModuleCacheAsync(tenantId, cancellationToken);
        }

        return response;
    }
}
