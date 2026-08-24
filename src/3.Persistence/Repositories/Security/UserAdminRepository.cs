// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper implementation of <see cref="IUserAdminRepository"/>. All queries are
/// raw SQL against the SQL Server schema defined by SPEC 17 onward. None of the
/// queries filter on <c>IsActive</c> by design: this repository exists specifically
/// to surface and mutate users the EF global filter would otherwise hide.
/// </summary>
public sealed class UserAdminRepository(ISqlConnectionFactory connectionFactory) : IUserAdminRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    public async Task<int> RevokeActiveRefreshTokensAsync(Guid userId, DateTime utcNow, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE [Security].[UserRefreshTokens]
            SET IsRevoked = 1,
                LastModified = @UtcNow
            WHERE UserId = @UserId
              AND IsRevoked = 0
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { UserId = userId, UtcNow = utcNow },
            cancellationToken: ct));
    }

    public async Task<int> CloseActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct = default)
    {
        // Security.UserConnectionLogs is a connection audit trail (see migration
        // 20260514081921_InitialReset). Its column set is intentionally minimal —
        // Id, UserId, IpAddress, Country, UserAgent, ConnectionDate, IsActiveSession,
        // DisconnectionDate — and it does NOT carry audit columns (Created / CreatedBy /
        // LastModified / LastModifiedBy / GcRecord). The sibling query in
        // RoleUserSessionRepository.SoftRevokeActiveConnectionsAsync uses the same shape
        // and is the reference implementation; keep this one aligned with it.
        const string sql = """
            UPDATE [Security].[UserConnectionLogs]
            SET IsActiveSession = 0,
                DisconnectionDate = @UtcNow
            WHERE UserId = @UserId
              AND IsActiveSession = 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { UserId = userId, UtcNow = utcNow },
            cancellationToken: ct));
    }

    public async Task<UserAdminSnapshot?> GetAdminSnapshotAsync(Guid userId, Guid companyId, CancellationToken ct = default)
    {
        // Single trip: resolves HasMembership via EXISTS, keeps the snapshot flat for Dapper.
        const string sql = """
            SELECT u.Id          AS UserId,
                   u.Email       AS Email,
                   u.FirstName   AS FirstName,
                   u.IsActive    AS IsActive,
                   u.GcRecord    AS GcRecord,
                   CASE WHEN EXISTS (
                       SELECT 1
                       FROM [Security].[UserCompanies] uc
                       WHERE uc.UserId = u.Id
                         AND uc.CompanyId = @CompanyId
                         AND uc.GcRecord = 0
                   ) THEN 1 ELSE 0 END AS HasMembership
            FROM [Security].[Users] u
            WHERE u.Id = @UserId
              AND u.GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var row = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            new CommandDefinition(sql,
                new { UserId = userId, CompanyId = companyId },
                cancellationToken: ct));

        return row is null
            ? null
            : new UserAdminSnapshot(
                UserId: row.UserId,
                Email: row.Email,
                FirstName: row.FirstName,
                IsActive: row.IsActive,
                GcRecord: row.GcRecord,
                HasMembership: row.HasMembership != 0);
    }

    public async Task<bool> SetUserActiveStatusAsync(
        Guid userId,
        bool isActive,
        string reason,
        string? modifiedBy,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        const string sql = """
            UPDATE [Security].[Users]
            SET IsActive = @IsActive,
                StatusChangeReason = @Reason,
                LastModified = @UtcNow,
                LastModifiedBy = @ModifiedBy
            WHERE Id = @UserId
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var affected = await connection.ExecuteAsync(new CommandDefinition(sql,
            new { UserId = userId, IsActive = isActive, Reason = reason, ModifiedBy = modifiedBy, UtcNow = utcNow },
            cancellationToken: ct));
        return affected == 1;
    }

    public async Task<bool> HasCompanyMembershipAsync(Guid userId, Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [Security].[UserCompanies]
                WHERE UserId = @UserId
                  AND CompanyId = @CompanyId
                  AND GcRecord = 0
            ) THEN 1 ELSE 0 END AS bit);
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { UserId = userId, CompanyId = companyId }, cancellationToken: ct));
        return result == 1;
    }

    public async Task<bool> HasAnyCompanyAsync(Guid userId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [Security].[UserCompanies]
                WHERE UserId = @UserId
                  AND GcRecord = 0
            ) THEN 1 ELSE 0 END AS bit);
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { UserId = userId }, cancellationToken: ct));
        return result == 1;
    }

    public async Task<IReadOnlyList<Guid>> FilterExistingRoleIdsAsync(
        IReadOnlyList<Guid> roleIds, Guid companyId, CancellationToken ct = default)
    {
        if (roleIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        const string sql = """
            SELECT r.Id
            FROM [Security].[Roles] r
            INNER JOIN [Security].[RoleCompanies] rc
                ON rc.RoleId = r.Id
               AND rc.CompanyId = @CompanyId
               AND rc.GcRecord = 0
            WHERE r.Id IN @RoleIds
              AND r.GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var rows = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { RoleIds = roleIds, CompanyId = companyId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<string?> GetCompanyNameAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT [Name]
            FROM [Common].[Companies]
            WHERE Id = @CompanyId
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        return await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));
    }

    /// <summary>Dapper mapping for the flat snapshot projection.</summary>
    private sealed class SnapshotRow
    {
        public Guid UserId { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public bool IsActive { get; set; }
        public int GcRecord { get; set; }
        public int HasMembership { get; set; }
    }
}
