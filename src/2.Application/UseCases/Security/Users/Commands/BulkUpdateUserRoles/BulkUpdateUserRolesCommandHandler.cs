using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.Users.Commands.BulkUpdateUserRoles;

/// <summary>
/// Applies a delta of role additions and removals to a batch of users inside the
/// caller's tenant. SPEC 28 / F6, item 20. Semantics:
///   * addRoleIds: rows missing for the user are inserted or reactivated (never
///     duplicated, so the unique (UserId, RoleId, CompanyId) index stays intact);
///   * removeRoleIds: active rows are soft-deleted;
///   * untouched roles are left alone.
/// Single round-trip per lookup (memberships, current role assignments, role
/// validity), one <see cref="IUnitOfWork.SaveAsync"/> flush, and one cache
/// invalidation per user whose outcome was <c>Updated</c>.
/// </summary>
public sealed class BulkUpdateUserRolesCommandHandler(
    ISqlConnectionFactory connectionFactory,
    IUserAdminRepository userAdminRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    IAuditLogger auditLogger,
    ILogger<BulkUpdateUserRolesCommandHandler> logger)
    : IRequestHandler<BulkUpdateUserRolesCommand, Response<BulkUpdateUserRolesResultDto>>
{
    // Reads every active + soft-deleted UserRoleCompany for the batch in one
    // round-trip. Including soft-deleted rows is what lets the add-branch
    // reactivate an existing row instead of colliding with the unique index.
    private const string AssignmentsForUsersSql = """
        SELECT urc.Id     AS Id,
               urc.UserId AS UserId,
               urc.RoleId AS RoleId,
               urc.GcRecord AS GcRecord
        FROM [Security].[UserRoleCompanies] urc
        WHERE urc.CompanyId = @CompanyId
          AND urc.UserId IN @UserIds;
        """;

    public async Task<Response<BulkUpdateUserRolesResultDto>> Handle(
        BulkUpdateUserRolesCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Tenant guard. The endpoint operates inside the caller's tenant only.
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<BulkUpdateUserRolesResultDto>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to bulk-update user roles."]);
        }

        var userIds = request.UserIds.ToArray();
        var addRoleIds = request.AddRoleIds ?? Array.Empty<Guid>();
        var removeRoleIds = request.RemoveRoleIds ?? Array.Empty<Guid>();

        // 2. Role validity check: every AddRoleId and RemoveRoleId must belong to
        //    the caller's tenant. A single bulk read covers both lists — it is an
        //    operator-level error, not data-level, so it cuts the entire batch.
        var combinedRoleIds = addRoleIds
            .Concat(removeRoleIds)
            .Distinct()
            .ToArray();

        var validRoleIds = combinedRoleIds.Length == 0
            ? Array.Empty<Guid>()
            : (await userAdminRepository.FilterExistingRoleIdsAsync(
                combinedRoleIds, companyId, cancellationToken)).ToArray();

        if (validRoleIds.Length != combinedRoleIds.Length)
        {
            return Response<BulkUpdateUserRolesResultDto>.Error(
                "ROLE_NOT_FOUND",
                ["One or more role ids do not belong to this tenant."]);
        }

        // 3. Membership lookup in one query (not N). Users without membership get
        //    `UserNotFound` in their item — they do NOT cut the batch.
        var membersSet = (await userAdminRepository.FilterUsersWithMembershipAsync(
            userIds, companyId, cancellationToken)).ToHashSet();

        // 4. Existing assignments for the batched members in one query.
        var memberIds = membersSet.ToArray();
        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        var assignments = memberIds.Length == 0
            ? new List<AssignmentRow>()
            : (await connection.QueryAsync<AssignmentRow>(
                new CommandDefinition(AssignmentsForUsersSql,
                    new { UserIds = memberIds, CompanyId = companyId },
                    cancellationToken: cancellationToken))).AsList();

        // Index once: per-user (RoleId → RowId, GcRecord). O(assignments) total.
        var activeByUserRole = new Dictionary<Guid, Dictionary<Guid, Guid>>();
        var softDeletedByUserRole = new Dictionary<Guid, Dictionary<Guid, Guid>>();
        foreach (var row in assignments)
        {
            if (row.GcRecord == BaseAuditableEntity.ActiveGcRecord)
            {
                if (!activeByUserRole.TryGetValue(row.UserId, out var byRole))
                {
                    byRole = new Dictionary<Guid, Guid>();
                    activeByUserRole[row.UserId] = byRole;
                }
                byRole[row.RoleId] = row.Id;
            }
            else
            {
                if (!softDeletedByUserRole.TryGetValue(row.UserId, out var byRole))
                {
                    byRole = new Dictionary<Guid, Guid>();
                    softDeletedByUserRole[row.UserId] = byRole;
                }
                byRole[row.RoleId] = row.Id;
            }
        }

        var addSet = new HashSet<Guid>(addRoleIds);
        var removeSet = new HashSet<Guid>(removeRoleIds);

        var roleRepository = unitOfWork.GetRepository<UserRoleCompany>();
        var utcNow = DateTime.UtcNow;

        var items = new List<BulkUpdateUserRoleItemDto>(userIds.Length);
        var usersUpdated = 0;
        var usersSkipped = 0;

        foreach (var userId in userIds)
        {
            if (!membersSet.Contains(userId))
            {
                items.Add(new BulkUpdateUserRoleItemDto(
                    UserId: userId,
                    Outcome: BulkRoleOutcome.UserNotFound,
                    RolesAdded: 0,
                    RolesRemoved: 0));
                usersSkipped++;
                continue;
            }

            activeByUserRole.TryGetValue(userId, out var activeForUser);
            softDeletedByUserRole.TryGetValue(userId, out var softDeletedForUser);
            activeForUser ??= new Dictionary<Guid, Guid>();
            softDeletedForUser ??= new Dictionary<Guid, Guid>();

            var rolesAdded = 0;
            var rolesRemoved = 0;

            // -- Add branch: insert or reactivate. --
            foreach (var roleId in addRoleIds)
            {
                if (activeForUser.ContainsKey(roleId))
                {
                    continue;
                }

                if (softDeletedForUser.TryGetValue(roleId, out var reusableId))
                {
                    var reusable = await roleRepository.GetAsync(reusableId);
                    if (reusable is not null)
                    {
                        reusable.GcRecord = BaseAuditableEntity.ActiveGcRecord;
                        reusable.LastModified = utcNow;
                        reusable.LastModifiedBy = currentUserService.UserId;
                        await roleRepository.UpdateAsync(reusable);
                    }
                }
                else
                {
                    var fresh = new UserRoleCompany
                    {
                        UserId = userId,
                        RoleId = roleId,
                        CompanyId = companyId,
                        Created = utcNow,
                        CreatedBy = currentUserService.UserId
                    };
                    await roleRepository.InsertAsync(fresh);
                }

                rolesAdded++;
            }

            // -- Remove branch: soft-delete active assignments only. --
            foreach (var (roleId, rowId) in activeForUser)
            {
                if (!removeSet.Contains(roleId))
                {
                    continue;
                }

                var entity = await roleRepository.GetAsync(rowId);
                if (entity is not null)
                {
                    entity.MarkAsDeleted(utcNow);
                    entity.LastModified = utcNow;
                    entity.LastModifiedBy = currentUserService.UserId;
                    await roleRepository.UpdateAsync(entity);
                }

                rolesRemoved++;
            }

            var outcome = rolesAdded + rolesRemoved > 0
                ? BulkRoleOutcome.Updated
                : BulkRoleOutcome.NoChange;

            items.Add(new BulkUpdateUserRoleItemDto(
                UserId: userId,
                Outcome: outcome,
                RolesAdded: rolesAdded,
                RolesRemoved: rolesRemoved));

            if (outcome == BulkRoleOutcome.Updated)
            {
                usersUpdated++;
            }
        }

        // 6. Single atomic flush inside the open TransactionBehavior scope.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Bitácora de seguridad — one row per URC that effectively changed across the
        // batch. All rows share the same bulkOperationId so the UI can group them as
        // "operator X changed N roles across M users in one go". Only users whose
        // outcome was Updated contribute rows.
        var bulkOperationId = Guid.NewGuid();
        var bulkMetadata = $"{{\"bulkOperationId\":\"{bulkOperationId}\"}}";

        var auditEntries = new List<AuditLogEntryRequest>();
        foreach (var userItem in items.Where(i => i.Outcome == BulkRoleOutcome.Updated))
        {
            var uid = userItem.UserId;
            var userEmail = uid.ToString();

            if (!activeByUserRole.TryGetValue(uid, out var activeForUser))
            {
                continue;
            }

            foreach (var roleId in addRoleIds)
            {
                if (activeForUser.ContainsKey(roleId))
                {
                    continue;
                }
                var rowId = softDeletedByUserRole.TryGetValue(uid, out var sForUser) && sForUser.TryGetValue(roleId, out var reusable)
                    ? reusable
                    : Guid.NewGuid();
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.UserRoleCompany,
                    rowId,
                    AuditAction.Created,
                    EntityLabel: $"{userEmail} → {roleId}",
                    MetadataJson: bulkMetadata));
            }

            foreach (var (roleId, rowId) in activeForUser)
            {
                if (!removeSet.Contains(roleId))
                {
                    continue;
                }
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.UserRoleCompany,
                    rowId,
                    AuditAction.Deleted,
                    EntityLabel: $"{userEmail} → {roleId}",
                    MetadataJson: bulkMetadata));
            }
        }

        if (auditEntries.Count > 0)
        {
            await auditLogger.LogManyAsync(auditEntries, cancellationToken);
        }

        // 7. Cache invalidation only for users whose roles actually changed. Each
        //    invalidation is independently try/caught — a Redis blip must not
        //    revert the writes or fail the request.
        foreach (var item in items)
        {
            if (item.Outcome != BulkRoleOutcome.Updated)
            {
                continue;
            }

            try
            {
                await permissionService.InvalidateUserCacheAsync(
                    companyId, item.UserId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to invalidate permission cache for user {UserId} in company {CompanyId} after bulk role update.",
                    item.UserId,
                    companyId);
            }
        }

        return new Response<BulkUpdateUserRolesResultDto>
        {
            IsSuccess = true,
            Message = "Bulk role update processed.",
            Data = new BulkUpdateUserRolesResultDto(
                Items: items,
                UsersUpdated: usersUpdated,
                UsersSkipped: usersSkipped)
        };
    }

    private sealed class AssignmentRow
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
        public int GcRecord { get; set; }
    }
}