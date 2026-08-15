using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the SMS phone-verification codes stored under
/// <c>Security.PhoneVerificationCodes</c>. Dapper-backed; all writes are soft via the
/// project's <c>GcRecord</c> convention.
/// </summary>
public interface IPhoneVerificationCodeRepository
{
    /// <summary>
    /// Soft-deletes every active verification code for the user so only one outstanding
    /// code lives per user at any time.
    /// </summary>
    Task<int> InvalidateActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Inserts a freshly minted verification code row.
    /// </summary>
    Task InsertAsync(PhoneVerificationCode code, CancellationToken ct);

    /// <summary>
    /// Returns the user's most recently issued code whose <c>UsedAtUtc IS NULL</c>
    /// AND <c>ExpiresAtUtc &gt;= utcNow</c> AND attempt count is below the lock threshold
    /// (handled by the caller).
    /// </summary>
    Task<PhoneVerificationCode?> GetLatestActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Marks the supplied code row consumed. Returns <c>false</c> when the row is already
    /// used or no longer exists.
    /// </summary>
    Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Atomically increments the attempt counter. Returns the new value.
    /// </summary>
    Task<int> IncrementAttemptAsync(Guid codeId, DateTime utcNow, CancellationToken ct);
}
