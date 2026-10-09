using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Handles <see cref="RestoreRegionCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the region's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreRegionCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreRegionCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreRegionCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Region>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Country>(entity.CountryId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Region>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.CountryId == entity.CountryId && (x.Name == entity.Name || (entity.Code != null && x.Code == entity.Code))),
            cancellationToken: cancellationToken);
    }
}
