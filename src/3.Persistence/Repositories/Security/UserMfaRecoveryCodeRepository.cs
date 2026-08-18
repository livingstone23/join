// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed implementation of <see cref="IUserMfaRecoveryCodeRepository"/>.
/// Soft-deletes follow the project convention by stamping <c>GcRecord</c> with the UTC date integer.
/// </summary>
public sealed class UserMfaRecoveryCodeRepository(ISqlConnectionFactory connectionFactory) : IUserMfaRecoveryCodeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<int> InsertManyAsync(IEnumerable<UserMfaRecoveryCode> codes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(codes);

        const string sql = """
            INSERT INTO [Security].[UserMfaRecoveryCodes]
                (Id, UserId, CodeHash, CreatedAtUtc, UsedAtUtc, Created, CreatedBy, LastModified, LastModifiedBy, GcRecord)
            VALUES
                (@Id, @UserId, @CodeHash, @CreatedAtUtc, @UsedAtUtc, @Created, @CreatedBy, @LastModified, @LastModifiedBy, @GcRecord);
            """;

        var rows = codes.ToList();
        if (rows.Count == 0)
        {
            return 0;
        }

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            foreach (var row in rows)
            {
                await connection.ExecuteAsync(new CommandDefinition(sql, row, transaction, cancellationToken: ct));
            }

            transaction.Commit();
            return rows.Count;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserMfaRecoveryCode>> ListUnusedByUserAsync(Guid userId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                Id,
                UserId,
                CodeHash,
                CreatedAtUtc,
                UsedAtUtc
            FROM [Security].[UserMfaRecoveryCodes]
            WHERE UserId = @userId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0
            ORDER BY CreatedAtUtc, Id;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<UserMfaRecoveryCode>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
        return rows.AsList();
    }

    /// <inheritdoc />
    public async Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[UserMfaRecoveryCodes]
            SET UsedAtUtc = @utcNow,
                LastModified = @utcNow
            WHERE Id = @codeId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { codeId, utcNow }, cancellationToken: ct));
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteAllByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        // Soft-delete via GcRecord stamp so the audit trail survives.
        var gcStamp = int.Parse(utcNow.ToString("yyyyMMdd"));

        const string sql = """
            UPDATE [Security].[UserMfaRecoveryCodes]
            SET GcRecord = @gcStamp,
                LastModified = @utcNow
            WHERE UserId = @userId
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { userId, utcNow, gcStamp },
            cancellationToken: ct));
    }
}
