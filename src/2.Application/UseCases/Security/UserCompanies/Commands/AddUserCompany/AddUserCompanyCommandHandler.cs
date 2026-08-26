using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.AddUserCompany;

/// <summary>
/// Adds (or refreshes) a <c>UserCompany</c> membership and assigns the supplied roles
/// inside the target tenant. SPEC 28 / F3, item 18. The endpoint is SuperAdmin-only
/// (cross-tenant by design), so the tenant used for role validation is the
/// <c>CompanyId</c> from the body, not the caller's tenant from the JWT. Soft-deleted
/// rows are reactivated in place to avoid unique-index collisions on
/// <c>(UserId, CompanyId)</c> and <c>(UserId, RoleId, CompanyId)</c>.
/// </summary>
public sealed class AddUserCompanyCommandHandler(
    ISqlConnectionFactory connectionFactory,
    IUserAdminRepository userAdminRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    IAuditLogger auditLogger)
    : IRequestHandler<AddUserCompanyCommand, Response<AddUserCompanyResultDto>>
{
    // Lightweight index of every membership row (active + soft-deleted) for the
    // (userId, companyId) pair. Reading the GcRecord alongside the Id is what lets
    // guard 5 reactivate a soft-deleted row in place instead of inserting.
    private const string MembershipIndexSql = """
        SELECT Id        AS Id,
               IsDefault AS IsDefault,
               GcRecord  AS GcRecord
        FROM [Security].[UserCompanies]
        WHERE UserId = @UserId
          AND CompanyId = @CompanyId;
        """;

    // Same trick for UserRoleCompany: include soft-deleted rows so the
    // upsert-or-reactivate path has the row Id to reuse.
    private const string RoleAssignmentIndexSql = """
        SELECT Id       AS Id,
               RoleId   AS RoleId,
               GcRecord AS GcRecord
        FROM [Security].[UserRoleCompanies]
        WHERE UserId = @UserId
          AND CompanyId = @CompanyId;
        """;

    public async Task<Response<AddUserCompanyResultDto>> Handle(
        AddUserCompanyCommand request,
        CancellationToken cancellationToken)
    {
        // 1. User must exist and not be soft-deleted. The snapshot does NOT filter
        //    by IsActive (admin endpoints need to surface inactive accounts).
        var snapshot = await userAdminRepository.GetAdminSnapshotAsync(
            request.UserId, request.CompanyId, cancellationToken);
        if (snapshot is null)
        {
            return Response<AddUserCompanyResultDto>.Error(
                "USER_NOT_FOUND",
                ["User does not exist or is soft-deleted."]);
        }

        // 2. Company must exist and not be soft-deleted.
        var companyExists = await userAdminRepository.CompanyExistsAsync(
            request.CompanyId, cancellationToken);
        if (!companyExists)
        {
            return Response<AddUserCompanyResultDto>.Error(
                "COMPANY_NOT_FOUND",
                ["Company does not exist or is soft-deleted."]);
        }

        // 3. Every role must belong to the *body*'s CompanyId (this is a SuperAdmin
        //    endpoint — the caller's tenant from the JWT is irrelevant here).
        var existingRoleIds = await userAdminRepository.FilterExistingRoleIdsAsync(
            request.RoleIds, request.CompanyId, cancellationToken);
        if (existingRoleIds.Count != request.RoleIds.Count)
        {
            return Response<AddUserCompanyResultDto>.Error(
                "ROLE_NOT_FOUND",
                ["One or more role ids do not belong to the target company."]);
        }

        var requestedRoleSet = new HashSet<Guid>(request.RoleIds);

        // -- Read state in a single connection pass to keep round-trips low. --
        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        var membershipIndex = (await connection.QueryAsync<MembershipIndexRow>(
            new CommandDefinition(MembershipIndexSql,
                new { UserId = request.UserId, CompanyId = request.CompanyId },
                cancellationToken: cancellationToken))).AsList();

        var activeMembership = membershipIndex.FirstOrDefault(row =>
            row.GcRecord == BaseAuditableEntity.ActiveGcRecord);
        var softDeletedMembership = membershipIndex.FirstOrDefault(row =>
            row.GcRecord != BaseAuditableEntity.ActiveGcRecord);

        var roleIndex = (await connection.QueryAsync<RoleAssignmentIndexRow>(
            new CommandDefinition(RoleAssignmentIndexSql,
                new { UserId = request.UserId, CompanyId = request.CompanyId },
                cancellationToken: cancellationToken))).AsList();

        var membershipRepository = unitOfWork.GetRepository<UserCompany>();
        var roleRepository = unitOfWork.GetRepository<UserRoleCompany>();
        var utcNow = DateTime.UtcNow;
        bool isDefault;

        if (activeMembership is not null)
        {
            // Guard 4: existing active membership — keep IsDefault, replace roles.
            isDefault = activeMembership.IsDefault;
        }
        else if (softDeletedMembership is not null)
        {
            // Guard 5: reactivate the soft-deleted membership in place. Insert would
            // collide with the unique (UserId, CompanyId) index.
            var entity = await membershipRepository.GetAsync(softDeletedMembership.Id);
            isDefault = !await userAdminRepository.HasAnyCompanyAsync(
                request.UserId, cancellationToken);

            if (entity is not null)
            {
                entity.GcRecord = BaseAuditableEntity.ActiveGcRecord;
                entity.IsDefault = isDefault;
                entity.LastModified = utcNow;
                entity.LastModifiedBy = currentUserService.UserId;
                await membershipRepository.UpdateAsync(entity);
            }
        }
        else
        {
            // Guard 6: brand-new membership — IsDefault = true only when the user has
            // no other active company in any tenant.
            isDefault = !await userAdminRepository.HasAnyCompanyAsync(
                request.UserId, cancellationToken);

            var fresh = new UserCompany
            {
                UserId = request.UserId,
                CompanyId = request.CompanyId,
                IsDefault = isDefault,
                Created = utcNow,
                CreatedBy = currentUserService.UserId
            };
            await membershipRepository.InsertAsync(fresh);
        }

        // -- Roles: soft-delete every active assignment outside the requested set,
        //    reactivate soft-deleted assignments inside it, otherwise insert. --
        var activeByRoleId = roleIndex
            .Where(row => row.GcRecord == BaseAuditableEntity.ActiveGcRecord)
            .ToDictionary(row => row.RoleId, row => row.Id);

        var softDeletedByRoleId = roleIndex
            .Where(row => row.GcRecord != BaseAuditableEntity.ActiveGcRecord)
            .ToDictionary(row => row.RoleId, row => row.Id);

        // Remove: every active assignment whose role is no longer requested.
        foreach (var (roleId, rowId) in activeByRoleId)
        {
            if (requestedRoleSet.Contains(roleId))
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
        }

        // Add: reactivate a soft-deleted row in place, otherwise insert.
        foreach (var roleId in request.RoleIds)
        {
            if (activeByRoleId.ContainsKey(roleId))
            {
                continue;
            }

            if (softDeletedByRoleId.TryGetValue(roleId, out var reusableId))
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
                    UserId = request.UserId,
                    RoleId = roleId,
                    CompanyId = request.CompanyId,
                    Created = utcNow,
                    CreatedBy = currentUserService.UserId
                };
                await roleRepository.InsertAsync(fresh);
            }
        }

        // Single atomic flush inside the open TransactionBehavior scope.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Drop the permission snapshot cache so the next call rebuilds from the
        // fresh UserRoleCompanies state.
        await permissionService.InvalidateUserCacheAsync(
            request.CompanyId, request.UserId, cancellationToken);

        // Bitácora de seguridad — log membership + every URC that effectively changed.
        var userEmail = snapshot.Email ?? string.Empty;
        var companyLabel = $"{userEmail} @ {request.CompanyId}";

        var auditEntries = new List<AuditLogEntryRequest>();

        if (activeMembership is null)
        {
            // Either freshly inserted or a reactivation of a soft-deleted row — both surface
            // to the operator as "the user is now in this tenant" so log Created.
            auditEntries.Add(new AuditLogEntryRequest(
                AuditedEntity.UserCompany,
                Guid.NewGuid(),
                AuditAction.Created,
                EntityLabel: companyLabel,
                NewValues: new Dictionary<string, object?>
                {
                    ["UserId"] = request.UserId,
                    ["CompanyId"] = request.CompanyId,
                    ["IsDefault"] = isDefault
                }));
        }

        // URC rows that effectively changed: every role that was active before this call
        // and is NOT in the requested set got soft-deleted; every role that was not active
        // and IS in the requested set got inserted or reactivated.
        foreach (var (roleId, rowId) in activeByRoleId)
        {
            if (!requestedRoleSet.Contains(roleId))
            {
                auditEntries.Add(new AuditLogEntryRequest(
                    AuditedEntity.UserRoleCompany,
                    rowId,
                    AuditAction.Deleted,
                    EntityLabel: $"{userEmail} → {roleId}"));
            }
        }

        foreach (var roleId in request.RoleIds)
        {
            if (activeByRoleId.ContainsKey(roleId))
            {
                continue;
            }
            auditEntries.Add(new AuditLogEntryRequest(
                AuditedEntity.UserRoleCompany,
                softDeletedByRoleId.TryGetValue(roleId, out var reusableId) ? reusableId : Guid.NewGuid(),
                AuditAction.Created,
                EntityLabel: $"{userEmail} → {roleId}"));
        }

        if (auditEntries.Count > 0)
        {
            await auditLogger.LogManyAsync(auditEntries, cancellationToken);
        }

        return new Response<AddUserCompanyResultDto>
        {
            IsSuccess = true,
            Message = "User company membership added.",
            Data = new AddUserCompanyResultDto(
                UserId: request.UserId,
                CompanyId: request.CompanyId,
                IsDefault: isDefault,
                RoleIdsAssigned: request.RoleIds.ToArray())
        };
    }

    private sealed class MembershipIndexRow
    {
        public Guid Id { get; set; }
        public bool IsDefault { get; set; }
        public int GcRecord { get; set; }
    }

    private sealed class RoleAssignmentIndexRow
    {
        public Guid Id { get; set; }
        public Guid RoleId { get; set; }
        public int GcRecord { get; set; }
    }
}