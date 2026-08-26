using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Users.Commands.ReplaceUserRoles;

/// <summary>
/// Handles replacement of the full role set of a user inside the caller's tenant.
/// Rewritten by SPEC 28 / F2 to write to <c>Security.UserRoleCompanies</c> (the table
/// that governs authorization in <c>PermissionService</c> and <c>LoginCommandHandler</c>)
/// instead of <c>Security.UserRoles</c> (Identity). The external contract — request
/// shape <c>{ roles: ["Admin"] }</c> and response <c>Response&lt;UserWithRolesDto&gt;</c>
/// — is unchanged.
/// </summary>
/// <remarks>
/// Tenant comes from the JWT (SPEC 23). Role names are resolved to ids against
/// <c>Security.Roles ∩ Security.RoleCompanies</c> for the caller's tenant — a name
/// that exists in another tenant is rejected. Re-adding a previously soft-deleted
/// row reuses the existing row (reactivates <c>GcRecord = 0</c>) so the unique index
/// <c>(UserId, RoleId, CompanyId)</c> never collides. All writes go through EF Core
/// so the transactional scope opened by <c>TransactionBehavior</c> (SPEC 27 F0)
/// includes them.
/// </remarks>
public sealed class ReplaceUserRolesCommandHandler(
    ISqlConnectionFactory connectionFactory,
    IUserAdminRepository userAdminRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    IAuditLogger auditLogger)
    : IRequestHandler<ReplaceUserRolesCommand, Response<UserWithRolesDto>>
{
    private const string UserLookupSql = """
        SELECT Id          AS Id,
               UserName    AS UserName,
               Email       AS Email,
               IsActive    AS IsActive
        FROM [Security].[Users]
        WHERE Id = @UserId
          AND GcRecord = 0;
        """;

    // Returns the Id of every (active or soft-deleted) assignment so the
    // upsert-or-reactivate branch can decide whether to Update the existing row or
    // Insert a new one. Includes soft-deleted rows on purpose — without them, the
    // re-add branch would collide on the unique (UserId, RoleId, CompanyId) index
    // the first time a previously removed role is re-assigned.
    private const string AssignmentIndexSql = """
        SELECT Id       AS Id,
               RoleId   AS RoleId,
               GcRecord AS GcRecord
        FROM [Security].[UserRoleCompanies]
        WHERE UserId = @UserId
          AND CompanyId = @CompanyId;
        """;

    private const string CurrentAssignmentNamesSql = """
        SELECT urc.RoleId AS RoleId,
               r.Name     AS RoleName
        FROM [Security].[UserRoleCompanies] urc
        INNER JOIN [Security].[Roles] r
            ON r.Id = urc.RoleId
           AND r.GcRecord = 0
        WHERE urc.UserId = @UserId
          AND urc.CompanyId = @CompanyId
          AND urc.GcRecord = 0
        ORDER BY r.Name;
        """;

    public async Task<Response<UserWithRolesDto>> Handle(
        ReplaceUserRolesCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<UserWithRolesDto>.Error(
                "TENANT_REQUIRED",
                ["A tenant context is required to replace user roles."]);
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        // 1. User existence + identity. Replaces the former userManager.FindByIdAsync
        //    so the EF global query filter cannot hide soft-deleted users from us.
        var userRow = await connection.QuerySingleOrDefaultAsync<UserLookupRow>(
            new CommandDefinition(UserLookupSql, new { UserId = request.UserId }, cancellationToken: cancellationToken));
        if (userRow is null)
        {
            return Response<UserWithRolesDto>.Error(
                "USER_NOT_FOUND",
                ["User does not exist or is soft-deleted."]);
        }

        // 2. Normalize the requested names: trim, drop blanks, dedup case-insensitively.
        var requestedNames = (request.Roles ?? Array.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 3. Resolve names to ids against the calling tenant. A role whose name exists
        //    in another tenant but not in this one must be rejected.
        var upperNames = requestedNames
            .Select(name => name.ToUpperInvariant())
            .ToArray();

        var resolvedIds = upperNames.Length == 0
            ? Array.Empty<Guid>()
            : (await userAdminRepository.FilterExistingRoleIdsByNameAsync(
                upperNames, companyId, cancellationToken)).ToArray();

        if (resolvedIds.Length != upperNames.Length)
        {
            return Response<UserWithRolesDto>.Error(
                "ROLE_NOT_FOUND",
                ["One or more requested roles do not exist in this tenant."]);
        }

        var resolvedSet = new HashSet<Guid>(resolvedIds);

        // 4. Index of every (active + soft-deleted) assignment for this user/tenant.
        //    Read via Dapper for the lightweight (Id, RoleId, GcRecord) projection;
        //    the actual entity payloads are loaded via EF when we need to mutate them
        //    so the open transaction includes those writes.
        var index = (await connection.QueryAsync<AssignmentIndexRow>(
            new CommandDefinition(AssignmentIndexSql,
                new { UserId = request.UserId, CompanyId = companyId },
                cancellationToken: cancellationToken))).AsList();

        var activeIdsByRow = index
            .Where(row => row.GcRecord == BaseAuditableEntity.ActiveGcRecord)
            .ToDictionary(row => row.RoleId, row => row.Id);

        var softDeletedIdsByRoleId = index
            .Where(row => row.GcRecord != BaseAuditableEntity.ActiveGcRecord)
            .ToDictionary(row => row.RoleId, row => row.Id);

        var repository = unitOfWork.GetRepository<UserRoleCompany>();
        var utcNow = DateTime.UtcNow;

        // 5. Add — reactivate a soft-deleted row in place, otherwise insert a new one.
        foreach (var roleId in resolvedIds)
        {
            if (activeIdsByRow.ContainsKey(roleId))
            {
                continue;
            }

            if (softDeletedIdsByRoleId.TryGetValue(roleId, out var reusableId))
            {
                var reusable = await repository.GetAsync(reusableId);
                if (reusable is not null)
                {
                    reusable.GcRecord = BaseAuditableEntity.ActiveGcRecord;
                    reusable.LastModified = utcNow;
                    reusable.LastModifiedBy = currentUserService.UserId;
                    await repository.UpdateAsync(reusable);
                }
            }
            else
            {
                var fresh = new UserRoleCompany
                {
                    UserId = request.UserId,
                    RoleId = roleId,
                    CompanyId = companyId,
                    Created = utcNow,
                    CreatedBy = currentUserService.UserId
                };
                await repository.InsertAsync(fresh);
            }
        }

        // 6. Remove — soft-delete every active assignment whose role is no longer requested.
        foreach (var (roleId, rowId) in activeIdsByRow)
        {
            if (!resolvedSet.Contains(roleId))
            {
                var entity = await repository.GetAsync(rowId);
                if (entity is not null)
                {
                    entity.MarkAsDeleted(utcNow);
                    entity.LastModified = utcNow;
                    entity.LastModifiedBy = currentUserService.UserId;
                    await repository.UpdateAsync(entity);
                }
            }
        }

        // 7. Single atomic flush; TransactionBehavior (SPEC 27 F0) wraps this command
        //    in its own transaction so a failure mid-loop rolls back all writes.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // 8. Drop the permission snapshot cache so the next request rebuilds it from
        //    the new UserRoleCompanies state. SPEC 28 acceptance criterion: exactly
        //    one invalidation per successful request.
        await permissionService.InvalidateUserCacheAsync(companyId, request.UserId, cancellationToken);

        // 9. Project the freshly written assignment set for the response DTO. Names
        //    come from the DB (not from the request) so the DTO reflects what is
        //    actually persisted.
        var nameRows = (await connection.QueryAsync<AssignmentNameRow>(
            new CommandDefinition(CurrentAssignmentNamesSql,
                new { UserId = request.UserId, CompanyId = companyId },
                cancellationToken: cancellationToken))).AsList();

        var roleNames = nameRows
            .Select(row => row.RoleName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Bitácora de seguridad — log one UserRoleCompany row per role that effectively changed.
        // Resolved: roles in resolvedSet that were NOT active before → Created.
        // Removed:   roles that WERE active before but are NOT in resolvedSet → Deleted.
        var beforeActive = new HashSet<Guid>(activeIdsByRow.Keys);
        var auditEntries = new List<AuditLogEntryRequest>();
        var userEmail = userRow.Email ?? string.Empty;

        foreach (var roleId in resolvedIds)
        {
            if (beforeActive.Contains(roleId))
            {
                continue;
            }

            var rowId = softDeletedIdsByRoleId.TryGetValue(roleId, out var reusable) ? reusable : Guid.NewGuid();
            auditEntries.Add(new AuditLogEntryRequest(
                AuditedEntity.UserRoleCompany,
                rowId,
                AuditAction.Created,
                EntityLabel: $"{userEmail} → {roleId}"));
        }

        foreach (var roleId in beforeActive)
        {
            if (resolvedSet.Contains(roleId))
            {
                continue;
            }

            auditEntries.Add(new AuditLogEntryRequest(
                AuditedEntity.UserRoleCompany,
                activeIdsByRow[roleId],
                AuditAction.Deleted,
                EntityLabel: $"{userEmail} → {roleId}"));
        }

        if (auditEntries.Count > 0)
        {
            await auditLogger.LogManyAsync(auditEntries, cancellationToken);
        }

        return new Response<UserWithRolesDto>
        {
            IsSuccess = true,
            Message = "User roles updated successfully.",
            Data = new UserWithRolesDto
            {
                Id = userRow.Id,
                UserName = userRow.UserName ?? string.Empty,
                Email = userRow.Email ?? string.Empty,
                IsActive = userRow.IsActive,
                Roles = roleNames
            }
        };
    }

    private sealed class UserLookupRow
    {
        public Guid Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; }
    }

    private sealed class AssignmentIndexRow
    {
        public Guid Id { get; set; }
        public Guid RoleId { get; set; }
        public int GcRecord { get; set; }
    }

    private sealed class AssignmentNameRow
    {
        public Guid RoleId { get; set; }
        public string? RoleName { get; set; }
    }
}