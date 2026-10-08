using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles <see cref="RestorePersonFinancialProfileCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the financial profile's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestorePersonFinancialProfileCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestorePersonFinancialProfileCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestorePersonFinancialProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<PersonFinancialProfile>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Person>(entity.PersonId)
                || await restorer.IsParentDeletedAsync<IncomeRange>(entity.IncomeRangeId),
            // SPEC 41: if another active row already holds the IsCurrent flag, the restored row comes back without it.
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsCurrent && await restorer.AnyAsync<PersonFinancialProfile>(
                    x => x.GcRecord == 0 && x.Id != entity.Id && x.PersonId == entity.PersonId && x.IsCurrent))
                {
                    entity.Archive();
                }
            },
            cancellationToken: cancellationToken);
    }
}
