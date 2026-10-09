using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Handles <see cref="RestoreProjectCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the project's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreProjectCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProjectCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreProjectCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Project>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<EntityStatus>(entity.EntityStatusId),
            cancellationToken: cancellationToken);
    }
}
