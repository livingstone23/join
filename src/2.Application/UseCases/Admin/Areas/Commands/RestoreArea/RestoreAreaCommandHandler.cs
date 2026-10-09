using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Handles <see cref="RestoreAreaCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the area's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreAreaCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreAreaCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreAreaCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Area>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<EntityStatus>(entity.EntityStatusId),
            cancellationToken: cancellationToken);
    }
}
