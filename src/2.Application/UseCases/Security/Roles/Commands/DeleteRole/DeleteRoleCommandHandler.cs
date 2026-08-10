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
