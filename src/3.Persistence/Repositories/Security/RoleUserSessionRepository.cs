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
            SELECT Id, Type FROM (
                SELECT CAST(Id AS uuid) AS Id, CAST(0 AS int) AS Type
                FROM Security.UserRefreshTokens
                WHERE Id = @sessionId AND IsRevoked = FALSE AND GcRecord = 0
                UNION ALL
                SELECT CAST(Id AS uuid) AS Id, CAST(1 AS int) AS Type
                FROM Security.UserConnectionLogs
                WHERE Id = @sessionId AND IsActiveSession = TRUE AND GcRecord = 0
            ) AS s
            LIMIT 1;
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
            SELECT UserId FROM (
                SELECT UserId FROM Security.UserRefreshTokens
                WHERE Id = @sessionId AND GcRecord = 0
                UNION ALL
                SELECT UserId FROM Security.UserConnectionLogs
                WHERE Id = @sessionId AND GcRecord = 0
            ) AS s
            LIMIT 1;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<Guid?>(
            new CommandDefinition(sql, new { sessionId }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> SoftRevokeRefreshTokensExceptAsync(Guid userId, Guid? currentRefreshTokenId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE Security.UserRefreshTokens
            SET IsRevoked = TRUE,
                LastModified = @utcNow
            WHERE UserId = @userId
              AND IsRevoked = FALSE
              AND ExpiryDate > @utcNow
              AND GcRecord = 0
              AND Id <> COALESCE(@currentRefreshTokenId, CAST('00000000-0000-0000-0000-000000000000' AS uuid));
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
            UPDATE Security.UserConnectionLogs
            SET IsActiveSession = FALSE,
                DisconnectionDate = @utcNow
            WHERE UserId = @userId
              AND IsActiveSession = TRUE
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
            UPDATE Security.UserRefreshTokens
            SET IsRevoked = TRUE,
                LastModified = @utcNow
            WHERE Id = @refreshTokenId
              AND IsRevoked = FALSE
              AND GcRecord = 0
              AND Id <> COALESCE(@excludeCurrentRefreshTokenId, CAST('00000000-0000-0000-0000-000000000000' AS uuid));
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
            UPDATE Security.UserConnectionLogs
            SET IsActiveSession = FALSE,
                DisconnectionDate = @utcNow
            WHERE Id = @connectionId
              AND IsActiveSession = TRUE
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
            FROM Security.UserRoleCompanies urc
            INNER JOIN Security.Roles r
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
                SELECT CAST(TRIM(v) AS uuid) AS RoleId
                FROM unnest(string_to_array(@roleIdsCsv, ',')) AS v
                WHERE TRIM(v) <> ''
            ),
            ModuleOptions AS (
                SELECT
                    mo.Id   AS ModuleId,
                    mo.Name AS ModuleName,
                    so.Id   AS SystemOptionId,
                    so.Name AS OptionName,
                    so.Route AS OptionRoute,
                    COALESCE(so.OrderMenu, 0) AS DisplayOrder,
                    CAST(so.CanRead AS boolean) AS SupportCanRead,
                    CAST(so.CanCreate AS boolean) AS SupportCanCreate,
                    CAST(so.CanUpdate AS boolean) AS SupportCanUpdate,
                    CAST(so.CanDelete AS boolean) AS SupportCanDelete,
                    CAST(so.CanDownload AS boolean) AS SupportCanDownload,
                    CAST(so.CanExport AS boolean) AS SupportCanExport,
                    CAST(so.CanExecute AS boolean) AS SupportCanExecute
                FROM Security.SystemOptions so
                INNER JOIN Admin.SystemModules mo
                    ON mo.Id = so.ModuleId AND mo.GcRecord = 0
                WHERE so.GcRecord = 0
                  AND mo.IsActive = TRUE
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
                CAST(COALESCE(MAX(CASE WHEN rso.CanRead = TRUE     THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanRead,
                CAST(COALESCE(MAX(CASE WHEN rso.CanCreate = TRUE   THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanCreate,
                CAST(COALESCE(MAX(CASE WHEN rso.CanUpdate = TRUE   THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanUpdate,
                CAST(COALESCE(MAX(CASE WHEN rso.CanDelete = TRUE   THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanDelete,
                CAST(COALESCE(MAX(CASE WHEN rso.CanDownload = TRUE THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanDownload,
                CAST(COALESCE(MAX(CASE WHEN rso.CanExport = TRUE   THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanExport,
                CAST(COALESCE(MAX(CASE WHEN rso.CanExecute = TRUE  THEN 1 ELSE 0 END), 0) AS boolean) AS GrantedCanExecute
            FROM ModuleOptions mo
            LEFT JOIN Security.RoleSystemOptions rso
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
        var flat = await connection.QueryAsync<PermissionFlagGridFlatRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));
        return flat.Select(MapToGridRow).AsList();
    }

    /// <summary>
    /// Flat projection used to deserialize the CTE result set directly from Dapper.
    /// The 7 supports + 7 granted flags travel as separate columns; the public
    /// <see cref="PermissionFlagGridRow"/> wraps them in nested record types so consumers
    /// can pass them straight to <see cref="RoleSystemOptionMatrixOptionDto"/>.
    /// </summary>
    private sealed class PermissionFlagGridFlatRow
    {
        public Guid ModuleId { get; set; }
        public string ModuleName { get; set; } = string.Empty;
        public Guid SystemOptionId { get; set; }
        public string OptionName { get; set; } = string.Empty;
        public string OptionRoute { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool SupportCanRead { get; set; }
        public bool SupportCanCreate { get; set; }
        public bool SupportCanUpdate { get; set; }
        public bool SupportCanDelete { get; set; }
        public bool SupportCanDownload { get; set; }
        public bool SupportCanExport { get; set; }
        public bool SupportCanExecute { get; set; }
        public bool GrantedCanRead { get; set; }
        public bool GrantedCanCreate { get; set; }
        public bool GrantedCanUpdate { get; set; }
        public bool GrantedCanDelete { get; set; }
        public bool GrantedCanDownload { get; set; }
        public bool GrantedCanExport { get; set; }
        public bool GrantedCanExecute { get; set; }
    }

    /// <summary>
    /// Lifts the flat row into the public DTO shape.
    /// </summary>
    private static PermissionFlagGridRow MapToGridRow(PermissionFlagGridFlatRow flat) => new(
        ModuleId: flat.ModuleId,
        ModuleName: flat.ModuleName,
        SystemOptionId: flat.SystemOptionId,
        OptionName: flat.OptionName,
        OptionRoute: flat.OptionRoute,
        DisplayOrder: flat.DisplayOrder,
        Supports: new RoleSystemOptionSupportFlags(
            flat.SupportCanRead, flat.SupportCanCreate, flat.SupportCanUpdate,
            flat.SupportCanDelete, flat.SupportCanDownload, flat.SupportCanExport, flat.SupportCanExecute),
        Granted: new RoleSystemOptionGrantedFlags(
            flat.GrantedCanRead, flat.GrantedCanCreate, flat.GrantedCanUpdate,
            flat.GrantedCanDelete, flat.GrantedCanDownload, flat.GrantedCanExport, flat.GrantedCanExecute));
}
