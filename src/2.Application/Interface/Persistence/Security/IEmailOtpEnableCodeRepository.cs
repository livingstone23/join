using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

/// <summary>
/// Persistence contract for the authenticated email-OTP enable/disable codes stored under
/// <c>Security.EmailOtpEnableCodes</c>. Dapper-backed; same exact shape as
/// <see cref="IPhoneVerificationCodeRepository"/> — all writes are soft via the project's
/// <c>GcRecord</c> convention.
/// </summary>
public interface IEmailOtpEnableCodeRepository
{
    /// <summary>
    /// Soft-deletes every active code for the user so only one outstanding code lives per
    /// user at any time.
    /// </summary>
    Task<int> InvalidateActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct);

    /// <summary>
    /// Inserts a freshly minted email-OTP enable/disable code row.
    /// </summary>
    Task InsertAsync(EmailOtpEnableCode code, CancellationToken ct);

    /// <summary>
    /// Returns the user's most recently issued code whose <c>UsedAtUtc IS NULL</c> and
    /// <c>GcRecord = 0</c>. Deliberately does NOT filter on <c>ExpiresAtUtc</c> — an
    /// expired-but-otherwise-active row is still returned so the caller can tell
    /// <c>EMAIL_OTP_CODE_EXPIRED</c> apart from <c>EMAIL_OTP_NOT_REQUESTED</c> (no code was ever
    /// sent), which <c>enable</c>/<c>disable</c> report as two distinct error codes. The attempt
    /// count against the lock threshold is also the caller's responsibility.
    /// </summary>
    Task<EmailOtpEnableCode?> GetLatestActiveByUserAsync(Guid userId, DateTime utcNow, CancellationToken ct);

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
