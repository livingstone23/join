using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Handles <see cref="RestorePersonAddressCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the person address's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestorePersonAddressCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestorePersonAddressCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestorePersonAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<PersonAddress>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Person>(entity.PersonId)
                || await restorer.IsParentDeletedAsync<Country>(entity.CountryId)
                || await restorer.IsParentDeletedAsync<Province>(entity.ProvinceId)
                || await restorer.IsParentDeletedAsync<Municipality>(entity.MunicipalityId)
                || await restorer.IsParentDeletedAsync<StreetType>(entity.StreetTypeId)
                || (entity.RegionId is { } regionId && await restorer.IsParentDeletedAsync<Region>(regionId)),
            // SPEC 41: if another active row already holds the IsDefault flag, the restored row comes back without it.
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsDefault && await restorer.AnyAsync<PersonAddress>(
                    x => x.GcRecord == 0 && x.Id != entity.Id && x.PersonId == entity.PersonId && x.IsDefault))
                {
                    entity.RemoveDefault();
                }
            },
            cancellationToken: cancellationToken);
    }
}
