using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Application.UseCases.Security.SystemOptions;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Handles <see cref="RestoreSystemModuleCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the system module's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreSystemModuleCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    SystemOptionCascadeCoordinator cascadeCoordinator)
    : IRequestHandler<RestoreSystemModuleCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreSystemModuleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<SystemModule>(
            request.Id,
            request.CompanyId,
            // SPEC 41 (decision 2026-10-08): bring back the children deleted in the same cascade.
            restoreCascade: (entity, stamp, _) => cascadeCoordinator.RestoreSubtreeAsync(entity.Id, rootOptionId: null, stamp),
            cancellationToken: cancellationToken);
    }
}
