using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Handles <see cref="RestorePersonBusinessProfileCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the business profile's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestorePersonBusinessProfileCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestorePersonBusinessProfileCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestorePersonBusinessProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<PersonBusinessProfile>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Person>(entity.PersonId)
                || await restorer.IsParentDeletedAsync<Industry>(entity.IndustryId)
                || await restorer.IsParentDeletedAsync<TaxRegime>(entity.TaxRegimeId),
            // SPEC 41: if another active row already holds the IsActive flag, the restored row comes back without it.
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsActive && await restorer.AnyAsync<PersonBusinessProfile>(
                    x => x.GcRecord == 0 && x.Id != entity.Id && x.PersonId == entity.PersonId && x.IsActive))
                {
                    entity.Deactivate();
                }
            },
            cancellationToken: cancellationToken);
    }
}
