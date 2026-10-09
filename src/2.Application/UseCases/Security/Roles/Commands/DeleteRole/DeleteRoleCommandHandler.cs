using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using System.Linq.Expressions;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.Roles.Commands.DeleteRole;

/// <summary>
/// Handler that soft-deletes an ApplicationRole. System-default roles are protected with a descriptive error;
/// existing UserRoleCompany references are not enforced here (out of scope for this spec).
/// </summary>
public sealed class DeleteRoleCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService,
    IAuditLogger auditLogger,
    ILogger<DeleteRoleCommandHandler> logger)
    : IRequestHandler<DeleteRoleCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error("No se pudo identificar la compania del usuario actual para eliminar el rol.");
        }

        var existing = await roleRepository.GetByIdForUpdateAsync(request.Id, cancellationToken);
        if (existing is null)
        {
            return Response<bool>.Error("Rol no encontrado o inactivo.");
        }

        if (existing.IsSystemDefault)
        {
            return Response<bool>.Error("No se puede eliminar un rol del sistema. Es requerido para el funcionamiento de la aplicacion.");
        }

        // SPEC 41 (decision 2026-10-08): user assignments are references and block the delete in ANY
        // company (a role is shared across companies through RoleCompany). Before SPEC 41 only the caller's
        // tenant was counted, so a role still assigned in another company could be deleted.
        var roleId = existing.Id;
        var usersCount = (await unitOfWork.GetRepository<UserRoleCompany>()
            .GetAllIncludingDeletedAsync(urc => urc.GcRecord == 0 && urc.RoleId == roleId)).Count();
        if (usersCount > 0)
        {
            logger.LogWarning(
                "Refusing to delete role {RoleId} for company {CompanyId}: {UsersCount} active assignment(s).",
                existing.Id, currentUserService.CompanyId, usersCount);
            return Response<bool>.Error(
                "ROLE_HAS_USERS",
                [$"El rol tiene {usersCount} usuario(s) asignado(s). Desasigná antes de eliminar."]);
        }

        // Snapshot for the bitácora before stamping GcRecord — the diff should reflect the
        // row as it stood when the operator hit delete.
        var deletedLabel = existing.Name;
        var deletedOldValues = new Dictionary<string, object?>
        {
            ["Name"] = existing.Name,
            ["Description"] = existing.Description,
            ["IsSystemDefault"] = existing.IsSystemDefault
        };

        var modifiedBy = currentUserService.UserId ?? "system";

        // Stamp GcRecord with the yyyyMMdd UTC int, matching the project-wide soft-delete convention
        // (see BaseAuditableEntity doc + Country/Person/Project/... handlers).
        var deletedAtUtc = DateTime.UtcNow;
        existing.GcRecord = BaseAuditableEntity.GetDeletionGcRecordStamp(deletedAtUtc);
        existing.LastModified = deletedAtUtc;
        existing.LastModifiedBy = modifiedBy;

        // SPEC 41 (decision 2026-10-08): the role's permissions and company links are composition and are
        // soft-deleted with it, with the same stamp, so RestoreRole can bring them back together.
        await MarkAsDeletedAsync<RoleSystemOption>(o => o.GcRecord == 0 && o.RoleId == roleId, deletedAtUtc);
        await MarkAsDeletedAsync<RoleCompany>(rc => rc.GcRecord == 0 && rc.RoleId == roleId, deletedAtUtc);

        await roleRepository.UpdateAsync(existing, cancellationToken);
        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected == 0)
        {
            logger.LogWarning("Soft delete of role {RoleId} affected 0 rows. Race condition or already deleted.", existing.Id);
            return Response<bool>.Error("Rol no encontrado o inactivo.");
        }

        await auditLogger.LogAsync(
            AuditedEntity.Role,
            existing.Id,
            AuditAction.Deleted,
            entityLabel: deletedLabel,
            oldValues: deletedOldValues,
            ct: cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Role deleted successfully.",
            Data = true
        };
    }

    private async Task MarkAsDeletedAsync<T>(Expression<Func<T, bool>> predicate, DateTime deletedAtUtc)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var row in await repository.GetAllIncludingDeletedAsync(predicate))
        {
            row.MarkAsDeleted(deletedAtUtc);
            await repository.UpdateAsync(row);
        }
    }
}
