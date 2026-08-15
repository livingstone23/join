// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
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
}
