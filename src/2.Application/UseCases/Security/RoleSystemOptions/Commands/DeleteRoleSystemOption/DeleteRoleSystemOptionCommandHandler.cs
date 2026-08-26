using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Handles soft deletion of role-system-option permission rules.
/// The tenant is always derived from the authenticated caller via <see cref="ICurrentUserService"/>.
/// </summary>
public sealed class DeleteRoleSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IAuditLogger auditLogger)
    : IRequestHandler<DeleteRoleSystemOptionCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteRoleSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["CompanyId is required."]);
        }

        if (request.CompanyId.HasValue && request.CompanyId.Value != companyId)
        {
            return Response<Guid>.Error(
                "COMPANY_MISMATCH",
                ["CompanyId in the request does not match the authenticated tenant."]);
        }

        var repository = unitOfWork.RoleSystemOptions;
        var entity = await repository.GetTrackedActiveByIdAndCompanyAsync(request.Id, companyId, cancellationToken);
        if (entity is null)
        {
            return Response<Guid>.Error("ROLE_SYSTEM_OPTION_NOT_FOUND", ["Role system option not found."]);
        }

        // Snapshot flags + display names BEFORE stamping GcRecord — the post-delete
        // readback would skip the soft-deleted row.
        var oldValues = new Dictionary<string, object?>
        {
            ["CanRead"] = entity.CanRead,
            ["CanCreate"] = entity.CanCreate,
            ["CanUpdate"] = entity.CanUpdate,
            ["CanDelete"] = entity.CanDelete,
            ["CanDownload"] = entity.CanDownload,
            ["CanExport"] = entity.CanExport,
            ["CanExecute"] = entity.CanExecute,
            ["IsVisibleMenu"] = entity.IsVisibleMenu,
            ["OrderMenu"] = entity.OrderMenu
        };
        var names = await repository.GetNamesByIdAndCompanyAsync(entity.Id, companyId, cancellationToken);
        var deletedLabel = names is not null ? $"{names.RoleName} → {names.SystemOptionName}" : null;

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the permission rule."]);
        }

        await auditLogger.LogAsync(
            AuditedEntity.RoleSystemOption,
            entity.Id,
            AuditAction.Deleted,
            entityLabel: deletedLabel,
            oldValues: oldValues,
            ct: cancellationToken);

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Role system option deleted successfully.",
            Data = entity.Id
        };
    }
}