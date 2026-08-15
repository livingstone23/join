// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed repository for the AccountController session-revoke flows.
/// Reads cross <c>Security.UserConnectionLogs</c> and <c>Security.UserRefreshTokens</c> via UNION ALL.
/// All writes are soft (no DELETE statements).
/// </summary>
public sealed class RoleUserSessionRepository(ISqlConnectionFactory connectionFactory) : IRoleUserSessionRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<SessionLookupResult?> FindActiveByIdAsync(Guid sessionId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) [Id], [Type] FROM (
                SELECT CAST(Id AS uniqueidentifier) AS Id, CAST(0 AS int) AS [Type]
                FROM [Security].[UserRefreshTokens]
                WHERE Id = @sessionId AND IsRevoked = 0 AND GcRecord = 0
                UNION ALL
                SELECT CAST(Id AS uniqueidentifier) AS Id, CAST(1 AS int) AS [Type]
                FROM [Security].[UserConnectionLogs]
                WHERE Id = @sessionId AND IsActiveSession = 1 AND GcRecord = 0
            ) AS s;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<UnionRow>(
            new CommandDefinition(sql, new { sessionId }, cancellationToken: ct));
        if (row is null)
        {
            return null;
        }

        return new SessionLookupResult(row.Id, (SessionType)row.Type);
    }

    /// <inheritdoc />
    public async Task<Guid?> GetUserIdBySessionIdAsync(Guid sessionId, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1) UserId FROM (
                SELECT UserId FROM [Security].[UserRefreshTokens]
                WHERE Id = @sessionId AND GcRecord = 0
                UNION ALL
                SELECT UserId FROM [Security].[UserConnectionLogs]
                WHERE Id = @sessionId AND GcRecord = 0
            ) AS s;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { sessionId }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> SoftRevokeRefreshTokensExceptAsync(Guid userId, Guid? currentRefreshTokenId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[UserRefreshTokens]
            SET IsRevoked = 1,
                LastModified = @utcNow
            WHERE UserId = @userId
              AND IsRevoked = 0
              AND ExpiryDate > @utcNow
              AND GcRecord = 0
              AND Id <> ISNULL(@currentRefreshTokenId, '00000000-0000-0000-0000-000000000000');
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { userId, currentRefreshTokenId, utcNow },
            cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> SoftRevokeActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[UserConnectionLogs]
            SET IsActiveSession = 0,
                DisconnectionDate = @utcNow
            WHERE UserId = @userId
              AND IsActiveSession = 1
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { userId, utcNow },
            cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> SoftRevokeSingleRefreshTokenAsync(Guid refreshTokenId, Guid? excludeCurrentRefreshTokenId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[UserRefreshTokens]
            SET IsRevoked = 1,
                LastModified = @utcNow
            WHERE Id = @refreshTokenId
              AND IsRevoked = 0
              AND GcRecord = 0
              AND Id <> ISNULL(@excludeCurrentRefreshTokenId, '00000000-0000-0000-0000-000000000000');
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { refreshTokenId, excludeCurrentRefreshTokenId, utcNow },
            cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> SoftRevokeSingleConnectionAsync(Guid connectionId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[UserConnectionLogs]
            SET IsActiveSession = 0,
                DisconnectionDate = @utcNow
            WHERE Id = @connectionId
              AND IsActiveSession = 1
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { connectionId, utcNow },
            cancellationToken: ct));
    }

    /// <summary>
    /// Internal Dapper mapping for <see cref="FindActiveByIdAsync"/>.
    /// </summary>
    private sealed class UnionRow
    {
        public Guid Id { get; set; }
        public int Type { get; set; }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRoleLookupRow>> ListUserRolesInTenantAsync(
        Guid userId,
        Guid companyId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT DISTINCT
                urc.RoleId AS RoleId,
                r.Name AS RoleName
            FROM [Security].[UserRoleCompanies] urc
            INNER JOIN [Security].[Roles] r
                ON r.Id = urc.RoleId AND r.GcRecord = 0
            WHERE urc.UserId = @userId
              AND urc.CompanyId = @companyId
              AND urc.GcRecord = 0
            ORDER BY r.Name;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<(Guid RoleId, string RoleName)>(
            new CommandDefinition(sql, new { userId, companyId }, cancellationToken: ct));
        return rows.Select(r => new UserRoleLookupRow(r.RoleId, r.RoleName)).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionFlagGridRow>> GetPermissionFlagGridAsync(
        IReadOnlyCollection<Guid> roleIds,
        Guid companyId,
        CancellationToken ct)
    {
        // Single CTE-driven SELECT that joins RoleSystemOptions + SystemOptions + SystemModules
        // for the supplied role ids inside the tenant. When roleIds is empty the LEFT JOIN
        // collapses and COALESCE keeps Granted = all-false so the UI still receives every
        // active option (per F4 acceptance criteria: no-roles case).
        const string sql = """
            WITH EffectiveRoles AS (
                SELECT CAST(LTRIM(RTRIM([value])) AS uniqueidentifier) AS RoleId
                FROM STRING_SPLIT(@roleIdsCsv, ',')
                WHERE LTRIM(RTRIM([value])) <> ''
            ),
            ModuleOptions AS (
                SELECT
                    mo.Id   AS ModuleId,
                    mo.Name AS ModuleName,
                    so.Id   AS SystemOptionId,
                    so.Name AS OptionName,
                    so.Route AS OptionRoute,
                    ISNULL(so.OrderMenu, 0) AS DisplayOrder,
                    CAST(so.CanRead AS bit) AS SupportCanRead,
                    CAST(so.CanCreate AS bit) AS SupportCanCreate,
                    CAST(so.CanUpdate AS bit) AS SupportCanUpdate,
                    CAST(so.CanDelete AS bit) AS SupportCanDelete,
                    CAST(so.CanDownload AS bit) AS SupportCanDownload,
                    CAST(so.CanExport AS bit) AS SupportCanExport,
                    CAST(so.CanExecute AS bit) AS SupportCanExecute
                FROM [Security].[SystemOptions] so
                INNER JOIN [Security].[SystemModules] mo
                    ON mo.Id = so.SystemModuleId AND mo.GcRecord = 0
                WHERE so.CompanyId = @companyId
                  AND so.GcRecord = 0
            )
            SELECT
                mo.ModuleId,
                mo.ModuleName,
                mo.SystemOptionId,
                mo.OptionName,
                mo.OptionRoute,
                mo.DisplayOrder,
                mo.SupportCanRead, mo.SupportCanCreate, mo.SupportCanUpdate,
                mo.SupportCanDelete, mo.SupportCanDownload, mo.SupportCanExport, mo.SupportCanExecute,
                CAST(COALESCE(MAX(CASE WHEN rso.CanRead = 1     THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanRead,
                CAST(COALESCE(MAX(CASE WHEN rso.CanCreate = 1   THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanCreate,
                CAST(COALESCE(MAX(CASE WHEN rso.CanUpdate = 1   THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanUpdate,
                CAST(COALESCE(MAX(CASE WHEN rso.CanDelete = 1   THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanDelete,
                CAST(COALESCE(MAX(CASE WHEN rso.CanDownload = 1 THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanDownload,
                CAST(COALESCE(MAX(CASE WHEN rso.CanExport = 1   THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanExport,
                CAST(COALESCE(MAX(CASE WHEN rso.CanExecute = 1  THEN 1 ELSE 0 END), 0) AS bit) AS GrantedCanExecute
            FROM ModuleOptions mo
            LEFT JOIN [Security].[RoleSystemOptions] rso
                ON rso.SystemOptionId = mo.SystemOptionId
               AND rso.CompanyId = @companyId
               AND rso.GcRecord = 0
               AND rso.RoleId IN (SELECT RoleId FROM EffectiveRoles)
            GROUP BY
                mo.ModuleId, mo.ModuleName,
                mo.SystemOptionId, mo.OptionName, mo.OptionRoute, mo.DisplayOrder,
                mo.SupportCanRead, mo.SupportCanCreate, mo.SupportCanUpdate,
                mo.SupportCanDelete, mo.SupportCanDownload, mo.SupportCanExport, mo.SupportCanExecute
            ORDER BY mo.ModuleId, mo.DisplayOrder, mo.SystemOptionId;
            """;

        var parameters = new
        {
            companyId,
            roleIdsCsv = roleIds.Count == 0 ? string.Empty : string.Join(',', roleIds)
        };

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PermissionFlagGridRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return rows.AsList();
    }
}
