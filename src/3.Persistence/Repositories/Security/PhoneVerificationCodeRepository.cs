// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed implementation of <see cref="IPhoneVerificationCodeRepository"/>.
/// Invalidation stamps <c>GcRecord</c>; reads filter on it so soft-deleted codes never
/// influence future attempts.
/// </summary>
public sealed class PhoneVerificationCodeRepository(ISqlConnectionFactory connectionFactory) : IPhoneVerificationCodeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<int> InvalidateActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        var gcStamp = int.Parse(utcNow.ToString("yyyyMMdd"));

        const string sql = """
            UPDATE [Security].[PhoneVerificationCodes]
            SET GcRecord = @gcStamp,
                LastModified = @utcNow
            WHERE UserId = @userId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteAsync(new CommandDefinition(sql,
            new { userId, utcNow, gcStamp },
            cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task InsertAsync(PhoneVerificationCode code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(code);

        const string sql = """
            INSERT INTO [Security].[PhoneVerificationCodes]
                (Id, UserId, PhoneNumber, CodeHash, ExpiresAtUtc, UsedAtUtc, AttemptCount, Created, CreatedBy, LastModified, LastModifiedBy, GcRecord)
            VALUES
                (@Id, @UserId, @PhoneNumber, @CodeHash, @ExpiresAtUtc, @UsedAtUtc, @AttemptCount, @Created, @CreatedBy, @LastModified, @LastModifiedBy, @GcRecord);
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, code, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<PhoneVerificationCode?> GetLatestActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                Id,
                UserId,
                PhoneNumber,
                CodeHash,
                ExpiresAtUtc,
                UsedAtUtc,
                AttemptCount
            FROM [Security].[PhoneVerificationCodes]
            WHERE UserId = @userId
              AND UsedAtUtc IS NULL
              AND ExpiresAtUtc >= @utcNow
              AND GcRecord = 0
            ORDER BY ExpiresAtUtc DESC, Id DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<PhoneVerificationCode>(
            new CommandDefinition(sql, new { userId, utcNow }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[PhoneVerificationCodes]
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
    public async Task<int> IncrementAttemptAsync(Guid codeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[PhoneVerificationCodes]
            SET AttemptCount = AttemptCount + 1,
                LastModified = @utcNow
            WHERE Id = @codeId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0;

            SELECT AttemptCount FROM [Security].[PhoneVerificationCodes] WHERE Id = @codeId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { codeId, utcNow }, cancellationToken: ct));
    }
}
