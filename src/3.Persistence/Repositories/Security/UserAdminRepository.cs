// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.DTO.Security;
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
                   u.StatusChangeReason AS StatusChangeReason,
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
                HasMembership: row.HasMembership != 0,
                StatusChangeReason: row.StatusChangeReason);
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

    public async Task<bool> IsSuperAdminAsync(string? userId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out var parsed))
        {
            return false;
        }

        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [Security].[Users]
                WHERE Id = @UserId
                  AND GcRecord = 0
                  AND IsSuperAdmin = 1
            ) THEN 1 ELSE 0 END AS bit);
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { UserId = parsed }, cancellationToken: ct));
        return result == 1;
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

    public async Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct = default)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [Common].[Companies]
                WHERE Id = @CompanyId
                  AND GcRecord = 0
            ) THEN 1 ELSE 0 END AS bit);
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var result = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { CompanyId = companyId }, cancellationToken: ct));
        return result == 1;
    }

    public async Task<IReadOnlyList<Guid>> FilterExistingRoleIdsByNameAsync(
        IReadOnlyList<string> roleNames, Guid companyId, CancellationToken ct = default)
    {
        if (roleNames.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        // Caller normalizes names to upper-invariant before passing them in. The
        // INNER JOIN against RoleCompanies is what makes this tenant-scoped: a role
        // whose Name matches but that is not linked to the supplied tenant is filtered
        // out, which is exactly the semantics required by ReplaceUserRoles (the role
        // exists in another tenant and must not bleed into this one).
        const string sql = """
            SELECT r.Id
            FROM [Security].[Roles] r
            INNER JOIN [Security].[RoleCompanies] rc
                ON rc.RoleId = r.Id
               AND rc.CompanyId = @CompanyId
               AND rc.GcRecord = 0
            WHERE UPPER(r.Name) IN @RoleNames
              AND r.GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var rows = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { RoleNames = roleNames, CompanyId = companyId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<UserCompanyMembershipInfo?> GetMembershipInfoAsync(
        Guid userId, Guid companyId, CancellationToken ct = default)
    {
        // Single trip: exists flag, default flag, and the count of the user's active
        // companies. The count is needed by the last-company guard in
        // RemoveUserCompany; resolving it here avoids a second round-trip.
        //
        // The outer SELECT has no natural FROM source (every column is a scalar
        // subquery), so we anchor it on `FROM (VALUES(1)) AS dummy(x)`. The
        // trailing WHERE EXISTS on Users makes the whole row vanish when the
        // user is missing or soft-deleted, which QuerySingleOrDefaultAsync
        // maps to a null result.
        const string sql = """
            SELECT
                CAST(CASE WHEN EXISTS (
                    SELECT 1
                    FROM [Security].[UserCompanies]
                    WHERE UserId = @UserId
                      AND CompanyId = @CompanyId
                      AND GcRecord = 0
                ) THEN 1 ELSE 0 END AS bit) AS [Exists],
                CAST(ISNULL((
                    SELECT TOP(1) CASE WHEN IsDefault = 1 THEN 1 ELSE 0 END
                    FROM [Security].[UserCompanies]
                    WHERE UserId = @UserId
                      AND CompanyId = @CompanyId
                      AND GcRecord = 0
                ), 0) AS bit) AS [IsDefault],
                ISNULL((
                    SELECT COUNT(1)
                    FROM [Security].[UserCompanies]
                    WHERE UserId = @UserId
                      AND GcRecord = 0
                ), 0) AS [TotalActiveCompanies]
            FROM (VALUES(1)) AS dummy(x)
            WHERE EXISTS (
                SELECT 1
                FROM [Security].[Users]
                WHERE Id = @UserId
                  AND GcRecord = 0
            );
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var row = await connection.QuerySingleOrDefaultAsync<MembershipInfoRow>(
            new CommandDefinition(sql, new { UserId = userId, CompanyId = companyId }, cancellationToken: ct));

        return row is null
            ? null
            : new UserCompanyMembershipInfo(
                Exists: row.Exists,
                IsDefault: row.IsDefault,
                TotalActiveCompanies: row.TotalActiveCompanies);
    }

    public async Task<IReadOnlyList<Guid>> FilterUsersWithMembershipAsync(
        IReadOnlyList<Guid> userIds, Guid companyId, CancellationToken ct = default)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        const string sql = """
            SELECT DISTINCT UserId
            FROM [Security].[UserCompanies]
            WHERE UserId IN @UserIds
              AND CompanyId = @CompanyId
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        var rows = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { UserIds = userIds, CompanyId = companyId }, cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<UserEffectivePermissionsDto?> GetEffectivePermissionsAsync(
        Guid userId, Guid companyId, CancellationToken ct = default)
    {
        // Single round-trip via QueryMultipleAsync. Two result sets:
        //   1) Module × Option grid with the effective Granted = OR of the flags of
        //      every active role the user holds in the tenant. MAX(CAST(... AS int))
        //      is the SQL idiom for logical OR across grouped bit columns — MAX of
        //      0/1 collapses to 1 if any row contributes a 1.
        //   2) Role Ids + Names of those same assignments, parallel and ordered by
        //      role name to feed the DTO's RoleIds / RoleNames lists.
        //
        // Returns null only when the user does not exist or is soft-deleted. When the
        // user exists but has no active role in the tenant, returns the DTO with
        // empty role lists and every Granted flag collapsed to false (the LEFT JOINs
        // emit no RoleSystemOptions rows so MAX stays 0).
        const string matrixSql = """
            SELECT
                sm.Id                              AS ModuleId,
                sm.Name                            AS ModuleName,
                sm.[Order]                         AS ModuleOrder,
                so.Id                              AS SystemOptionId,
                so.Name                            AS Name,
                so.Route                           AS Route,
                so.OrderMenu                       AS OptionOrder,
                CAST(ISNULL(so.CanRead,     0) AS bit) AS SupportsCanRead,
                CAST(ISNULL(so.CanCreate,   0) AS bit) AS SupportsCanCreate,
                CAST(ISNULL(so.CanUpdate,   0) AS bit) AS SupportsCanUpdate,
                CAST(ISNULL(so.CanDelete,   0) AS bit) AS SupportsCanDelete,
                CAST(ISNULL(so.CanDownload, 0) AS bit) AS SupportsCanDownload,
                CAST(ISNULL(so.CanExport,   0) AS bit) AS SupportsCanExport,
                CAST(ISNULL(so.CanExecute,  0) AS bit) AS SupportsCanExecute,
                MAX(CAST(ISNULL(rso.CanRead,     0) AS int)) AS GrantedCanRead,
                MAX(CAST(ISNULL(rso.CanCreate,   0) AS int)) AS GrantedCanCreate,
                MAX(CAST(ISNULL(rso.CanUpdate,   0) AS int)) AS GrantedCanUpdate,
                MAX(CAST(ISNULL(rso.CanDelete,   0) AS int)) AS GrantedCanDelete,
                MAX(CAST(ISNULL(rso.CanDownload, 0) AS int)) AS GrantedCanDownload,
                MAX(CAST(ISNULL(rso.CanExport,   0) AS int)) AS GrantedCanExport,
                MAX(CAST(ISNULL(rso.CanExecute,  0) AS int)) AS GrantedCanExecute
            FROM [Admin].[SystemModules] sm
            LEFT JOIN [Security].[SystemOptions] so
                ON so.ModuleId = sm.Id
               AND so.GcRecord = 0
            LEFT JOIN [Security].[UserRoleCompanies] urc
                ON urc.UserId = @UserId
               AND urc.CompanyId = @CompanyId
               AND urc.GcRecord = 0
            LEFT JOIN [Security].[RoleSystemOptions] rso
                ON rso.RoleId = urc.RoleId
               AND rso.CompanyId = @CompanyId
               AND rso.GcRecord = 0
               AND rso.SystemOptionId = so.Id
            WHERE sm.GcRecord = 0
            GROUP BY
                sm.Id, sm.Name, sm.[Order],
                so.Id, so.Name, so.Route, so.OrderMenu,
                so.CanRead, so.CanCreate, so.CanUpdate, so.CanDelete,
                so.CanDownload, so.CanExport, so.CanExecute
            ORDER BY
                ISNULL(sm.[Order], 2147483647) ASC,
                sm.Name ASC,
                ISNULL(so.OrderMenu, 2147483647) ASC,
                so.Name ASC;
            """;

        const string rolesSql = """
            SELECT r.Id   AS RoleId,
                   r.Name AS RoleName
            FROM [Security].[UserRoleCompanies] urc
            INNER JOIN [Security].[Roles] r
                ON r.Id = urc.RoleId
               AND r.GcRecord = 0
            WHERE urc.UserId = @UserId
              AND urc.CompanyId = @CompanyId
              AND urc.GcRecord = 0
            ORDER BY r.Name;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(
                $"{matrixSql}; {rolesSql};",
                new { UserId = userId, CompanyId = companyId },
                cancellationToken: ct));

        var matrixRows = (await grid.ReadAsync<EffectivePermissionRow>()).AsList();
        var roleRows = (await grid.ReadAsync<EffectivePermissionRoleRow>()).AsList();

        // The matrix query emits a sentinel of the user-exists check via the WHERE
        // EXISTS on Users. When the user does not exist, the query returns no rows
        // — same shape as "user exists with no permissions". We disambiguate by
        // asking the DB directly when no module rows came back.
        var userExists = matrixRows.Count > 0 || (await HasAnyMatrixRowsAsync(
            connection, userId, companyId, ct));

        if (!userExists)
        {
            return null;
        }

        var modules = matrixRows
            .GroupBy(row => new { row.ModuleId, row.ModuleName, row.ModuleOrder })
            .OrderBy(g => g.Key.ModuleOrder ?? int.MaxValue)
            .ThenBy(g => g.Key.ModuleName)
            .Select(g => new RoleSystemOptionMatrixModuleDto(
                ModuleId: g.Key.ModuleId,
                ModuleName: g.Key.ModuleName,
                Options: g
                    .OrderBy(row => row.OptionOrder ?? int.MaxValue)
                    .ThenBy(row => row.Name)
                    .Select(row => new RoleSystemOptionMatrixOptionDto(
                        SystemOptionId: row.SystemOptionId,
                        Name: row.Name,
                        Route: row.Route,
                        Supports: new RoleSystemOptionSupportFlags(
                            row.SupportsCanRead, row.SupportsCanCreate, row.SupportsCanUpdate,
                            row.SupportsCanDelete, row.SupportsCanDownload, row.SupportsCanExport,
                            row.SupportsCanExecute),
                        Granted: new RoleSystemOptionGrantedFlags(
                            row.GrantedCanRead != 0, row.GrantedCanCreate != 0,
                            row.GrantedCanUpdate != 0, row.GrantedCanDelete != 0,
                            row.GrantedCanDownload != 0, row.GrantedCanExport != 0,
                            row.GrantedCanExecute != 0)))
                    .ToList()))
            .ToList();

        var roleIds = roleRows.Select(r => r.RoleId).ToList();
        var roleNames = roleRows.Select(r => r.RoleName).ToList();

        return new UserEffectivePermissionsDto(
            UserId: userId,
            CompanyId: companyId,
            RoleIds: roleIds,
            RoleNames: roleNames,
            Modules: modules);
    }

    /// <summary>
    /// Disambiguates "user does not exist" from "user exists but holds no roles in
    /// the tenant" when the matrix query returned zero rows. A cheap existence check
    /// against <c>Security.Users</c> tells the handler which case applies.
    /// </summary>
    private static async Task<bool> HasAnyMatrixRowsAsync(
        System.Data.IDbConnection connection,
        Guid userId,
        Guid companyId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT CAST(CASE WHEN EXISTS (
                SELECT 1
                FROM [Security].[Users]
                WHERE Id = @UserId
                  AND GcRecord = 0
            ) THEN 1 ELSE 0 END AS bit);
            """;

        var command = new CommandDefinition(sql, new { UserId = userId, CompanyId = companyId }, cancellationToken: ct);
        var exists = await connection.ExecuteScalarAsync<int>(command);
        return exists == 1;
    }

    private sealed class EffectivePermissionRow
    {
        public Guid ModuleId { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public int? ModuleOrder { get; set; }
        public Guid SystemOptionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public int? OptionOrder { get; set; }
        public bool SupportsCanRead { get; set; }
        public bool SupportsCanCreate { get; set; }
        public bool SupportsCanUpdate { get; set; }
        public bool SupportsCanDelete { get; set; }
        public bool SupportsCanDownload { get; set; }
        public bool SupportsCanExport { get; set; }
        public bool SupportsCanExecute { get; set; }
        public int GrantedCanRead { get; set; }
        public int GrantedCanCreate { get; set; }
        public int GrantedCanUpdate { get; set; }
        public int GrantedCanDelete { get; set; }
        public int GrantedCanDownload { get; set; }
        public int GrantedCanExport { get; set; }
        public int GrantedCanExecute { get; set; }
    }

    private sealed class EffectivePermissionRoleRow
    {
        public Guid RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
    }

    /// <summary>Dapper mapping for the flat snapshot projection.</summary>
    private sealed class SnapshotRow
    {
        public Guid UserId { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public bool IsActive { get; set; }
        public int GcRecord { get; set; }
        public string? StatusChangeReason { get; set; }
        public int HasMembership { get; set; }
    }

    /// <summary>Dapper mapping for the membership info projection.</summary>
    private sealed class MembershipInfoRow
    {
        public bool Exists { get; set; }
        public bool IsDefault { get; set; }
        public int TotalActiveCompanies { get; set; }
    }
}
