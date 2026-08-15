// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed repository for the append-only <c>Security.SecurityEventLogs</c> audit table.
/// Reads and writes via <see cref="ISqlConnectionFactory"/>.
/// </summary>
public sealed class SecurityEventRepository(ISqlConnectionFactory connectionFactory) : ISecurityEventRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<int> InsertAsync(SecurityEventLog entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        const string sql = """
            INSERT INTO [Security].[SecurityEventLogs]
                (Id, UserId, EventType, OccurredAtUtc, IpAddress, UserAgent, Result, MetadataJson)
            VALUES
                (@Id, @UserId, @EventType, @OccurredAtUtc, @IpAddress, @UserAgent, @Result, @MetadataJson);
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            Id = entry.Id,
            entry.UserId,
            entry.EventType,
            entry.OccurredAtUtc,
            entry.IpAddress,
            entry.UserAgent,
            entry.Result,
            entry.MetadataJson
        }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityEventLog>> ListByUserPagedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var offset = (pageNumber - 1) * pageSize;

        // Cross-DB pagination: SQL Server OFFSET/FETCH NEXT (SQLite/Postgres share the LIMIT/OFFSET shape).
        const string sql = """
            SELECT
                Id,
                UserId,
                EventType,
                OccurredAtUtc,
                IpAddress,
                UserAgent,
                Result,
                MetadataJson
            FROM [Security].[SecurityEventLogs]
            WHERE UserId = @UserId
            ORDER BY OccurredAtUtc DESC, Id DESC
            OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SecurityEventLog>(
            new CommandDefinition(sql, new { userId, offset, pageSize }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<int> CountByUserAsync(Guid userId, CancellationToken ct)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM [Security].[SecurityEventLogs]
            WHERE UserId = @userId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }
}
