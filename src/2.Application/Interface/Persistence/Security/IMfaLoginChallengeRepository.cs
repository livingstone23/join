using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the server-side MFA login challenges stored under
/// <c>Security.MfaLoginChallenges</c>. Dapper-backed; invalidation is soft via the
/// project's <c>GcRecord</c> convention.
/// </summary>
public interface IMfaLoginChallengeRepository
{
    /// <summary>
    /// Inserts a freshly minted login-challenge row.
    /// </summary>
    Task InsertAsync(MfaLoginChallenge challenge, CancellationToken ct);

    /// <summary>
    /// Looks up an active challenge by the hash of the opaque token the client sent back;
    /// excludes consumed or invalidated (<c>GcRecord != 0</c>) challenges. Deliberately does
    /// NOT exclude expired rows — the caller compares <c>ExpiresAtUtc</c> against
    /// <paramref name="utcNow"/> itself so <c>CHALLENGE_EXPIRED</c> can be reported distinctly
    /// from <c>CHALLENGE_NOT_FOUND</c>.
    /// </summary>
    Task<MfaLoginChallenge?> GetActiveByTokenHashAsync(string tokenHash, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Stores a freshly sent email challenge code for an active challenge.
    /// </summary>
    Task UpdateEmailCodeAsync(Guid challengeId, string emailCodeHash, DateTime expiresAtUtc, DateTime sentAtUtc, CancellationToken ct);

    /// <summary>
    /// Atomically increments the shared attempt counter (both methods count against the same
    /// challenge). Returns the new value.
    /// </summary>
    Task<int> IncrementAttemptAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Marks the challenge consumed after a successful <c>verify</c>. Returns <c>false</c> when
    /// the row is already consumed, invalidated, or no longer exists.
    /// </summary>
    Task<bool> MarkConsumedAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Invalidates the entire challenge (e.g. after the 5th failed attempt), regardless of which
    /// method was used for the failed attempts — the client must log in again from scratch.
    /// </summary>
    Task InvalidateAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);
}
