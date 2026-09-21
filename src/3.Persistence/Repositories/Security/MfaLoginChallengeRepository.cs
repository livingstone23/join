// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using Dapper;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;

namespace JOIN.Persistence.Repositories.Security;

/// <summary>
/// Dapper-backed implementation of <see cref="IMfaLoginChallengeRepository"/>.
/// Invalidation stamps <c>GcRecord</c>; reads filter on it so soft-deleted/invalidated
/// challenges never influence future attempts.
/// </summary>
public sealed class MfaLoginChallengeRepository(ISqlConnectionFactory connectionFactory) : IMfaLoginChallengeRepository
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task InsertAsync(MfaLoginChallenge challenge, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        const string sql = """
            INSERT INTO [Security].[MfaLoginChallenges]
                (Id, UserId, TargetCompanyId, TokenHash, ExpiresAtUtc, ConsumedAtUtc, AttemptCount,
                 EmailCodeHash, EmailCodeExpiresAtUtc, EmailSentAtUtc,
                 Created, CreatedBy, LastModified, LastModifiedBy, GcRecord)
            VALUES
                (@Id, @UserId, @TargetCompanyId, @TokenHash, @ExpiresAtUtc, @ConsumedAtUtc, @AttemptCount,
                 @EmailCodeHash, @EmailCodeExpiresAtUtc, @EmailSentAtUtc,
                 @Created, @CreatedBy, @LastModified, @LastModifiedBy, @GcRecord);
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(sql, challenge, cancellationToken: ct));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately does NOT filter on <c>ExpiresAtUtc</c> — "active" here means only
    /// "not consumed and not invalidated". An expired-but-otherwise-active row is still
    /// returned so the caller can tell <c>CHALLENGE_EXPIRED</c> apart from
    /// <c>CHALLENGE_NOT_FOUND</c> (genuinely unknown/consumed/invalidated token), which the
    /// acceptance criteria require as two distinct error codes. <paramref name="utcNow"/> is
    /// kept for signature parity with the spec and is not used to filter rows.
    /// </remarks>
    public async Task<MfaLoginChallenge?> GetActiveByTokenHashAsync(string tokenHash, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            SELECT
                Id,
                UserId,
                TargetCompanyId,
                TokenHash,
                ExpiresAtUtc,
                ConsumedAtUtc,
                AttemptCount,
                EmailCodeHash,
                EmailCodeExpiresAtUtc,
                EmailSentAtUtc
            FROM [Security].[MfaLoginChallenges]
            WHERE TokenHash = @tokenHash
              AND ConsumedAtUtc IS NULL
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<MfaLoginChallenge>(
            new CommandDefinition(sql, new { tokenHash }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task UpdateEmailCodeAsync(Guid challengeId, string emailCodeHash, DateTime expiresAtUtc, DateTime sentAtUtc, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[MfaLoginChallenges]
            SET EmailCodeHash = @emailCodeHash,
                EmailCodeExpiresAtUtc = @expiresAtUtc,
                EmailSentAtUtc = @sentAtUtc,
                LastModified = @sentAtUtc
            WHERE Id = @challengeId
              AND ConsumedAtUtc IS NULL
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { challengeId, emailCodeHash, expiresAtUtc, sentAtUtc },
            cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<int> IncrementAttemptAsync(Guid challengeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[MfaLoginChallenges]
            SET AttemptCount = AttemptCount + 1,
                LastModified = @utcNow
            WHERE Id = @challengeId
              AND ConsumedAtUtc IS NULL
              AND GcRecord = 0;

            SELECT AttemptCount FROM [Security].[MfaLoginChallenges] WHERE Id = @challengeId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { challengeId, utcNow }, cancellationToken: ct));
    }

    /// <inheritdoc />
    public async Task<bool> MarkConsumedAsync(Guid challengeId, DateTime utcNow, CancellationToken ct)
    {
        const string sql = """
            UPDATE [Security].[MfaLoginChallenges]
            SET ConsumedAtUtc = @utcNow,
                LastModified = @utcNow
            WHERE Id = @challengeId
              AND ConsumedAtUtc IS NULL
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { challengeId, utcNow }, cancellationToken: ct));
        return affected > 0;
    }

    /// <inheritdoc />
    public async Task InvalidateAsync(Guid challengeId, DateTime utcNow, CancellationToken ct)
    {
        var gcStamp = int.Parse(utcNow.ToString("yyyyMMdd"));

        const string sql = """
            UPDATE [Security].[MfaLoginChallenges]
            SET GcRecord = @gcStamp,
                LastModified = @utcNow
            WHERE Id = @challengeId
              AND GcRecord = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { challengeId, utcNow, gcStamp },
            cancellationToken: ct));
    }
}
