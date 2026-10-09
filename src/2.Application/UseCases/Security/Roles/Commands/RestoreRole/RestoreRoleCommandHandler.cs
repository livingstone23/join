using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Commands.RestoreRole;

/// <summary>
/// Handles <see cref="RestoreRoleCommand"/> (SPEC 41, Etapa 3). <see cref="ApplicationRole"/> is an Identity
/// type (it does not derive from <see cref="BaseAuditableEntity"/>), so this handler mirrors
/// <see cref="SoftDeleteRestorer"/> instead of delegating to it, with the same error codes. The role's
/// permissions (<see cref="RoleSystemOption"/>) and company links (<see cref="RoleCompany"/>) deleted in the
/// same cascade (same <c>GcRecord</c> stamp) come back with it, unless their own parent is still deleted or an
/// active duplicate link already exists — those simply stay deleted. A deleted role had no users (the delete
/// is blocked otherwise), so there is no permission cache to drop.
/// </summary>
public sealed class RestoreRoleCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreRoleCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreRoleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        var roleRepository = unitOfWork.GetRepository<ApplicationRole>();
        var role = await roleRepository.GetIncludingDeletedAsync(request.Id);
        if (role is null)
        {
            return Response<Guid>.Error("NOT_FOUND", [$"ApplicationRole '{request.Id}' was not found."]);
        }

        if (role.GcRecord == BaseAuditableEntity.ActiveGcRecord)
        {
            return Response<Guid>.Error("NOT_DELETED", [$"ApplicationRole '{request.Id}' is not deleted."]);
        }

        var stamp = role.GcRecord;
        var roleId = role.Id;

        var permissionRepository = unitOfWork.GetRepository<RoleSystemOption>();
        foreach (var permission in await permissionRepository.GetAllIncludingDeletedAsync(o => o.RoleId == roleId && o.GcRecord == stamp))
        {
            if (await restorer.IsParentDeletedAsync<SystemOption>(permission.SystemOptionId)
                || await restorer.IsParentDeletedAsync<Company>(permission.CompanyId))
            {
                continue;
            }

            permission.Restore();
            await permissionRepository.UpdateAsync(permission);
        }

        var linkRepository = unitOfWork.GetRepository<RoleCompany>();
        foreach (var link in await linkRepository.GetAllIncludingDeletedAsync(rc => rc.RoleId == roleId && rc.GcRecord == stamp))
        {
            var companyId = link.CompanyId;
            if (await restorer.IsParentDeletedAsync<Company>(companyId)
                || await restorer.AnyAsync<RoleCompany>(rc => rc.GcRecord == 0 && rc.RoleId == roleId && rc.CompanyId == companyId))
            {
                continue;
            }

            link.Restore();
            await linkRepository.UpdateAsync(link);
        }

        role.Restore();
        role.LastModified = DateTime.UtcNow;
        role.LastModifiedBy = currentUserService.UserId ?? "system";
        await roleRepository.UpdateAsync(role);

        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
        {
            return Response<Guid>.Error("RESTORE_FAILED", [$"No records were affected while restoring ApplicationRole '{request.Id}'."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Data = role.Id,
            Message = "ApplicationRole restored successfully."
        };
    }
}
