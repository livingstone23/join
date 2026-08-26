using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands.BulkUpsertRoleSystemOptions;

/// <summary>
/// Handler for <see cref="BulkUpsertRoleSystemOptionsCommand"/>.
/// Orchestrates: tenant check, role existence check, payload mapping to entities,
/// a single SQL transaction for the diff (insert / update / soft-delete),
/// and per-user cache invalidation for the affected set.
///
/// Cache invalidation only fires after <c>BulkUpsertAsync</c> commits successfully;
/// if the bulk throws, the DB is rolled back and we never invalidate (stale cache is
/// preferable to a clean cache pointing at inconsistent data).
/// </summary>
public sealed class BulkUpsertRoleSystemOptionsCommandHandler(
    IRoleSystemOptionsRepository roleSystemOptionsRepository,
    IRoleRepository roleRepository,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    IAuditLogger auditLogger,
    ILogger<BulkUpsertRoleSystemOptionsCommandHandler> logger)
    : IRequestHandler<BulkUpsertRoleSystemOptionsCommand, Response<BulkUpsertRoleSystemOptionsResult>>
{
    public async Task<Response<BulkUpsertRoleSystemOptionsResult>> Handle(
        BulkUpsertRoleSystemOptionsCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<BulkUpsertRoleSystemOptionsResult>.Error(
                "TENANT_REQUIRED",
                ["CompanyId is required from the authenticated token."]);
        }

        if (!await roleRepository.ExistsAndActiveAsync(request.RoleId, cancellationToken))
        {
            return Response<BulkUpsertRoleSystemOptionsResult>.Error(
                "ROLE_NOT_FOUND",
                [$"Role {request.RoleId} not found or inactive."]);
        }

        var userId = currentUserService.UserId;
        var createdAt = DateTime.UtcNow;

        var newItems = request.Items.Select(i =>
        {
            // BaseAuditableEntity's protected ctor already assigns a fresh Id; we don't override it.
            var entity = new RoleSystemOption
            {
                CompanyId = companyId,
                RoleId = request.RoleId,
                SystemOptionId = i.SystemOptionId,
                CanRead = i.CanRead,
                CanCreate = i.CanCreate,
                CanUpdate = i.CanUpdate,
                CanDelete = i.CanDelete,
                CanDownload = i.CanDownload,
                CanExport = i.CanExport,
                CanExecute = i.CanExecute,
                IsVisibleMenu = true,
                OrderMenu = 0,
                GcRecord = 0,
                CreatedBy = userId,
                Created = createdAt,
                LastModifiedBy = userId,
                LastModified = createdAt
            };
            return entity;
        }).ToList();

        // Bitácora: snapshot the pre-upsert active rows so the Updated diff has the old flags.
        // GetActiveByRoleAndCompanyAsync runs on its own connection (Dapper) — safe here because
        // no other handler holds a transaction against the table at this point.
        var preSnapshot = await roleSystemOptionsRepository.GetActiveByRoleAndCompanyAsync(
            request.RoleId, companyId, cancellationToken);
        var preByOption = preSnapshot.ToDictionary(x => x.SystemOptionId);

        IReadOnlyList<Guid> created;
        IReadOnlyList<Guid> updated;
        IReadOnlyList<Guid> removed;
        try
        {
            (created, updated, removed) = await roleSystemOptionsRepository.BulkUpsertAsync(
                request.RoleId, companyId, newItems, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "BulkUpsertRoleSystemOptions failed for role {RoleId} in company {CompanyId}.",
                request.RoleId, companyId);
            throw; // Do NOT invalidate cache when the DB diff didn't commit (see class summary).
        }

        // Bitácora: one row per affected SystemOptionId, all sharing the same bulkOperationId.
        var bulkOperationId = Guid.NewGuid();
        var auditMetadata = $"{{\"bulkOperationId\":\"{bulkOperationId}\"}}";
        var auditEntries = new List<AuditLogEntryRequest>();

        foreach (var opt in newItems)
        {
            if (created.Contains(opt.SystemOptionId))
            {
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.RoleSystemOption,
                    opt.Id,
                    AuditAction.Created,
                    MetadataJson: auditMetadata,
                    NewValues: SnapshotFlags(opt)));
            }
            else if (updated.Contains(opt.SystemOptionId) && preByOption.TryGetValue(opt.SystemOptionId, out var prior))
            {
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.RoleSystemOption,
                    opt.Id,
                    AuditAction.Updated,
                    MetadataJson: auditMetadata,
                    OldValues: SnapshotFlags(prior),
                    NewValues: SnapshotFlags(opt)));
            }
        }

        foreach (var removedId in removed)
        {
            // The bulk path doesn't return the removed row's id directly, but the preSnapshot
            // does carry each row by SystemOptionId; the Id we log is whatever was on file.
            if (preByOption.TryGetValue(removedId, out var prior))
            {
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.RoleSystemOption,
                    prior.Id,
                    AuditAction.Deleted,
                    MetadataJson: auditMetadata,
                    OldValues: SnapshotFlags(prior)));
            }
        }

        if (auditEntries.Count > 0)
        {
            await auditLogger.LogManyAsync(auditEntries, cancellationToken);
        }

        // Cache invalidation: only users that actually had this role in this tenant before the diff.
        // We snapshot the affected set BEFORE the diff would matter, but reading after the diff
        // still gives the same set because BulkUpsertAsync only soft-deletes RoleSystemOption rows
        // — it does not touch UserRoleCompany. The set is post-bulk for safety.
        IReadOnlyList<Guid> affectedUserIds;
        try
        {
            affectedUserIds = await roleRepository.GetActiveUserIdsByRoleIdAsync(
                request.RoleId, companyId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The DB diff already committed. Cache invalidation is best-effort.
            logger.LogWarning(ex,
                "Failed to enumerate users for cache invalidation after bulk upsert of role {RoleId}. Cache will refresh on TTL.",
                request.RoleId);
            affectedUserIds = Array.Empty<Guid>();
        }

        foreach (var user in affectedUserIds)
        {
            try
            {
                await permissionService.InvalidateUserCacheAsync(companyId, user, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "InvalidateUserCacheAsync failed for user {UserId} in company {CompanyId}.",
                    user, companyId);
                // Per-user failure is non-fatal; the bulk diff already committed.
            }
        }

        return new Response<BulkUpsertRoleSystemOptionsResult>
        {
            IsSuccess = true,
            Message = "RoleSystemOption bulk upsert completed.",
            Data = new BulkUpsertRoleSystemOptionsResult(created, updated, removed)
        };
    }

    private static Dictionary<string, object?> SnapshotFlags(RoleSystemOption r) => new()
    {
        ["CanRead"] = r.CanRead,
        ["CanCreate"] = r.CanCreate,
        ["CanUpdate"] = r.CanUpdate,
        ["CanDelete"] = r.CanDelete,
        ["CanDownload"] = r.CanDownload,
        ["CanExport"] = r.CanExport,
        ["CanExecute"] = r.CanExecute,
        ["IsVisibleMenu"] = r.IsVisibleMenu,
        ["OrderMenu"] = r.OrderMenu
    };
}