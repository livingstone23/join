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
    SoftDeleteRestorer restorer)
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

        return await restorer.RestoreAsync<CompanyModule>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Company>(entity.CompanyId)
                || await restorer.IsParentDeletedAsync<SystemModule>(entity.ModuleId),
            cancellationToken: cancellationToken);
    }
}
