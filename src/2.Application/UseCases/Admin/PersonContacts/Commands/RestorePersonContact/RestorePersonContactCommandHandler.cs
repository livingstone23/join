using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Handles <see cref="RestorePersonContactCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the person contact's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestorePersonContactCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestorePersonContactCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestorePersonContactCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<PersonContact>(
            request.Id,
            request.CompanyId,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Person>(entity.PersonId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<PersonContact>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.PersonId == entity.PersonId && x.ContactType == entity.ContactType && x.ContactValue == entity.ContactValue),
            // SPEC 41: if another active row already holds the IsPrimary flag, the restored row comes back without it.
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsPrimary && await restorer.AnyAsync<PersonContact>(
                    x => x.GcRecord == 0 && x.Id != entity.Id && x.PersonId == entity.PersonId && x.IsPrimary && x.ContactType == entity.ContactType))
                {
                    entity.RemovePrimary();
                }
            },
            cancellationToken: cancellationToken);
    }
}
