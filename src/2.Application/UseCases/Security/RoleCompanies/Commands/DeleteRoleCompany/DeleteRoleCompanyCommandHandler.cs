// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Handler that soft-deletes a RoleCompany link via <see cref="BaseAuditableEntity.MarkAsDeleted"/>.
/// Follows the project convention: load tracked entity, stamp GcRecord, set LastModified, persist via UoW.
/// Active UserRoleCompany rows referencing the deleted role are not blocked (only logged as a warning)
/// — full transactional cleanup is out of scope for this spec.
/// </summary>
public sealed class DeleteRoleCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    IRoleCompanyRepository roleCompanyRepository,
    ICurrentUserService currentUserService,
    IAuditLogger auditLogger,
    ILogger<DeleteRoleCompanyCommandHandler> logger)
    : IRequestHandler<DeleteRoleCompanyCommand, Response<bool>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
    private readonly IRoleCompanyRepository _roleCompanyRepository = roleCompanyRepository ?? throw new ArgumentNullException(nameof(roleCompanyRepository));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
    private readonly IAuditLogger _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    private readonly ILogger<DeleteRoleCompanyCommandHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<Response<bool>> Handle(DeleteRoleCompanyCommand request, CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<bool>.Error(
                "INVALID_COMPANY_ID",
                new[] { "El token no contiene un CompanyId válido." });
        }

        var existing = await _roleCompanyRepository.GetByIdForUpdateAsync(request.Id, tenantId, cancellationToken);
        if (existing is null)
        {
            return Response<bool>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "No se encontró el vínculo rol-empresa para la compañía del token." });
        }

        // Snapshot for the bitácora before stamping GcRecord.
        var deletedRoleId = existing.RoleId;
        var deletedLabel = $"{existing.RoleId} @ {tenantId}";

        // Soft delete via the project's standard helper (writes GcRecord = yyyyMMdd UTC int).
        existing.MarkAsDeleted();
        existing.LastModified = DateTime.UtcNow;
        existing.LastModifiedBy = _currentUserService.UserId;

        await _roleCompanyRepository.UpdateAsync(existing, cancellationToken);
        var affected = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (affected == 0)
        {
            _logger.LogWarning(
                "Soft delete of RoleCompany {RoleCompanyId} affected 0 rows (roleId={RoleId}, tenantId={TenantId}). Race or already deleted.",
                existing.Id, existing.RoleId, tenantId);
            return Response<bool>.Error(
                "ROLE_COMPANY_NOT_FOUND",
                new[] { "No se encontró el vínculo rol-empresa para la compañía del token." });
        }

        await _auditLogger.LogAsync(
            AuditedEntity.RoleCompany,
            existing.Id,
            AuditAction.Deleted,
            entityLabel: deletedLabel,
            oldValues: new Dictionary<string, object?>
            {
                ["RoleId"] = deletedRoleId,
                ["CompanyId"] = tenantId
            },
            ct: cancellationToken);

        // Visibility-only warning: a deleted RoleCompany can leave active UserRoleCompany rows pointing at the
        // same Role. Not blocking the delete; cleanup is delegated to a future spec.
        _logger.LogWarning(
            "Soft-deleted RoleCompany {RoleCompanyId} (roleId={RoleId}, tenantId={TenantId}). Verify no active UserRoleCompany rows reference the same role.",
            existing.Id, existing.RoleId, tenantId);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "RoleCompany deleted successfully.",
            Data = true
        };
    }
}
