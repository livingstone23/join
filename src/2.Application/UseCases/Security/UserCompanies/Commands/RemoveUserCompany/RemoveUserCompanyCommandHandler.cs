using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

/// <summary>
/// Removes a user's membership in a tenant: soft-deletes the <c>UserCompany</c> row
/// and every <c>UserRoleCompany</c> row tied to the same (user, company) pair.
/// SPEC 28 / F4, item 18. Guard order is documented in the spec: last-company
/// check beats default-company so a single-tenant user gets the more useful error.
/// All writes go through EF Core so the <c>TransactionBehavior</c> scope wraps the
/// whole batch atomically.
/// </summary>
public sealed class RemoveUserCompanyCommandHandler(
    ISqlConnectionFactory connectionFactory,
    IUserAdminRepository userAdminRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    IAuditLogger auditLogger)
    : IRequestHandler<RemoveUserCompanyCommand, Response<bool>>
{
    // Loads every active + soft-deleted UserRoleCompany row for the pair so the
    // soft-delete branch can iterate the Ids without an N+1 of GetAsync calls.
    private const string RoleAssignmentsIndexSql = """
        SELECT Id AS Id
        FROM [Security].[UserRoleCompanies]
        WHERE UserId = @UserId
          AND CompanyId = @CompanyId
          AND GcRecord = 0;
        """;

    public async Task<Response<bool>> Handle(
        RemoveUserCompanyCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve every guard flag in a single round-trip via the admin repo.
        var info = await userAdminRepository.GetMembershipInfoAsync(
            request.UserId, request.CompanyId, cancellationToken);

        // 1. Membership must currently be active.
        if (info is null || !info.Exists)
        {
            return Response<bool>.Error(
                "MEMBERSHIP_NOT_FOUND",
                ["User does not have an active membership in this company."]);
        }

        // 2. Last-company check runs first: with a single company, that company is
        //    necessarily the default, so this is the more useful error message.
        if (info.TotalActiveCompanies == 1)
        {
            return Response<bool>.Error(
                "CANNOT_REMOVE_LAST_COMPANY",
                ["A user must belong to at least one company."]);
        }

        // 3. Default-company check runs after the last-company check so the order
        //    above is the one the operator sees when both would otherwise apply.
        if (info.IsDefault)
        {
            return Response<bool>.Error(
                "CANNOT_REMOVE_DEFAULT_COMPANY",
                ["The default company must be changed before it can be removed."]);
        }

        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        // Fetch the active UserCompany row so we can soft-delete it through EF.
        // GetMembershipInfoAsync already confirmed it exists and is active.
        var membershipRow = await connection.QuerySingleAsync<MembershipRow>(
            new CommandDefinition(
                """
                SELECT Id AS Id
                FROM [Security].[UserCompanies]
                WHERE UserId = @UserId
                  AND CompanyId = @CompanyId
                  AND GcRecord = 0;
                """,
                new { UserId = request.UserId, CompanyId = request.CompanyId },
                cancellationToken: cancellationToken));

        var roleIndex = (await connection.QueryAsync<RoleIdRow>(
            new CommandDefinition(RoleAssignmentsIndexSql,
                new { UserId = request.UserId, CompanyId = request.CompanyId },
                cancellationToken: cancellationToken))).AsList();

        var membershipRepository = unitOfWork.GetRepository<UserCompany>();
        var roleRepository = unitOfWork.GetRepository<UserRoleCompany>();
        var utcNow = DateTime.UtcNow;

        // -- Soft-delete the UserCompany row via MarkAsDeleted (sets GcRecord to the
        //    yyyyMMdd stamp, never a literal "1"). --
        var membershipEntity = await membershipRepository.GetAsync(membershipRow.Id);
        if (membershipEntity is not null)
        {
            membershipEntity.MarkAsDeleted(utcNow);
            membershipEntity.LastModified = utcNow;
            membershipEntity.LastModifiedBy = currentUserService.UserId;
            await membershipRepository.UpdateAsync(membershipEntity);
        }

        // -- Soft-delete every active UserRoleCompany for the pair. --
        foreach (var row in roleIndex)
        {
            var roleEntity = await roleRepository.GetAsync(row.Id);
            if (roleEntity is not null)
            {
                roleEntity.MarkAsDeleted(utcNow);
                roleEntity.LastModified = utcNow;
                roleEntity.LastModifiedBy = currentUserService.UserId;
                await roleRepository.UpdateAsync(roleEntity);
            }
        }

        // Single atomic flush inside the open TransactionBehavior scope.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Drop the permission snapshot cache so the next call rebuilds from the
        // now-empty role set for this user inside this tenant.
        await permissionService.InvalidateUserCacheAsync(
            request.CompanyId, request.UserId, cancellationToken);

        // Bitácora de seguridad — UserCompany soft-deleted plus one UserRoleCompany per
        // assignment that was removed. Single batch keeps the round-trip count low.
        var companyLabel = $"{request.UserId} @ {request.CompanyId}";
        var auditEntries = new List<AuditLogEntryRequest>
        {
            new(AuditedEntity.UserCompany, membershipRow.Id, AuditAction.Deleted, EntityLabel: companyLabel)
        };

        foreach (var row in roleIndex)
        {
            auditEntries.Add(new AuditLogEntryRequest(
                AuditedEntity.UserRoleCompany,
                row.Id,
                AuditAction.Deleted,
                EntityLabel: $"{request.UserId} → {request.CompanyId}"));
        }

        await auditLogger.LogManyAsync(auditEntries, cancellationToken);

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "User company membership removed.",
            Data = true
        };
    }

    private sealed class MembershipRow
    {
        public Guid Id { get; set; }
    }

    private sealed class RoleIdRow
    {
        public Guid Id { get; set; }
    }
}