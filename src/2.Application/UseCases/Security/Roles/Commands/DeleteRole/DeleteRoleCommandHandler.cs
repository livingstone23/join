using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
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

        // Defense in depth: refuse to delete a role that still has active assignments in the caller's tenant.
        // The count is CompanyId-scoped (RoleRepository.CountActiveUsersByRoleIdAsync filters by tenant),
        // so users in other companies do not block the delete here.
        var usersCount = await roleRepository.CountActiveUsersByRoleIdAsync(
            existing.Id, currentUserService.CompanyId, cancellationToken);
        if (usersCount > 0)
        {
            logger.LogWarning(
                "Refusing to delete role {RoleId} for company {CompanyId}: {UsersCount} active assignment(s).",
                existing.Id, currentUserService.CompanyId, usersCount);
            return Response<bool>.Error(
                "ROLE_HAS_USERS",
                [$"El rol tiene {usersCount} usuario(s) asignado(s). Desasigná antes de eliminar."]);
        }

        var modifiedBy = currentUserService.UserId ?? "system";

        // Stamp GcRecord with the yyyyMMdd UTC int, matching the project-wide soft-delete convention
        // (see BaseAuditableEntity doc + Country/Person/Project/... handlers).
        existing.GcRecord = BaseAuditableEntity.GetDeletionGcRecordStamp();
        existing.LastModified = DateTime.UtcNow;
        existing.LastModifiedBy = modifiedBy;

        await roleRepository.UpdateAsync(existing, cancellationToken);
        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected == 0)
        {
            logger.LogWarning("Soft delete of role {RoleId} affected 0 rows. Race condition or already deleted.", existing.Id);
            return Response<bool>.Error("Rol no encontrado o inactivo.");
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Role deleted successfully.",
            Data = true
        };
    }
}
