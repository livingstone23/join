// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed implementation of <see cref="IEmailOtpEnableCodeRepository"/>.
/// Exact clone of <see cref="PhoneVerificationCodeRepository"/>'s shape, but for the
/// authenticated email-OTP enable/disable flow.
/// </summary>
public sealed class EmailOtpEnableCodeRepository(ISqlConnectionFactory connectionFactory) : IEmailOtpEnableCodeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<int> InvalidateActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        var gcStamp = int.Parse(utcNow.ToString("yyyyMMdd"));

        const string sql = """
            UPDATE [Security].[EmailOtpEnableCodes]
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
    public async Task InsertAsync(EmailOtpEnableCode code, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(code);

        const string sql = """
            INSERT INTO [Security].[EmailOtpEnableCodes]
                (Id, UserId, Email, CodeHash, ExpiresAtUtc, UsedAtUtc, AttemptCount, Created, CreatedBy, LastModified, LastModifiedBy, GcRecord)
            VALUES
                (@Id, @UserId, @Email, @CodeHash, @ExpiresAtUtc, @UsedAtUtc, @AttemptCount, @Created, @CreatedBy, @LastModified, @LastModifiedBy, @GcRecord);
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, code, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<EmailOtpEnableCode?> GetLatestActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP (1)
                Id,
                UserId,
                Email,
                CodeHash,
                ExpiresAtUtc,
                UsedAtUtc,
                AttemptCount
            FROM [Security].[EmailOtpEnableCodes]
            WHERE UserId = @userId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0
            ORDER BY ExpiresAtUtc DESC, Id DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<EmailOtpEnableCode>(
            new CommandDefinition(sql, new { userId }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[EmailOtpEnableCodes]
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
            UPDATE [Security].[EmailOtpEnableCodes]
            SET AttemptCount = AttemptCount + 1,
                LastModified = @utcNow
            WHERE Id = @codeId
              AND UsedAtUtc IS NULL
              AND GcRecord = 0;

            SELECT AttemptCount FROM [Security].[EmailOtpEnableCodes] WHERE Id = @codeId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { codeId, utcNow }, cancellationToken: ct));
    }
}
