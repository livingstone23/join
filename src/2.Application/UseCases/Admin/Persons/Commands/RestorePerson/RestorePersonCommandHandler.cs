using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Application.UseCases.Admin.Persons;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Persons.Commands;

/// <summary>
/// Handles <see cref="RestorePersonCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the person's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestorePersonCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    PersonCascadeCoordinator cascadeCoordinator)
    : IRequestHandler<RestorePersonCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestorePersonCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Person>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<IdentificationType>(entity.IdentificationTypeId)
                || (entity.GenderId is { } genderId && await restorer.IsParentDeletedAsync<Gender>(genderId)),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Person>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.IdentificationTypeId == entity.IdentificationTypeId && x.IdentificationNumber == entity.IdentificationNumber),
            // SPEC 41 (decision 2026-10-08): bring back the children deleted in the same cascade.
            restoreCascade: (entity, stamp, _) => cascadeCoordinator.RestoreChildrenAsync(entity.Id, stamp),
            cancellationToken: cancellationToken);
    }
}
