using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Handles <see cref="RestoreSystemOptionCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the system option's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreSystemOptionCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreSystemOptionCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreSystemOptionCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<SystemOption>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<SystemModule>(entity.ModuleId)
                || (entity.ParentId is { } parentId && await restorer.IsParentDeletedAsync<SystemOption>(parentId)),
            cancellationToken: cancellationToken);
    }
}
