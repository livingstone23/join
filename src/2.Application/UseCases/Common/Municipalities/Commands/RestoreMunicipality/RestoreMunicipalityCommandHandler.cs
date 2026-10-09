using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Handles <see cref="RestoreMunicipalityCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the municipality's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreMunicipalityCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreMunicipalityCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreMunicipalityCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Municipality>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Province>(entity.ProvinceId),
            cancellationToken: cancellationToken);
    }
}
