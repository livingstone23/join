using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Server-side, single-use challenge created by <c>POST /Users/login</c> when the user has
/// 1+ active 2FA method and consumed by <c>POST /auth/mfa/challenge/verify</c>. Clones the
/// <see cref="PhoneVerificationCode"/> pattern (SPEC 30): PBKDF2 hash via <c>RecoveryCodeHasher</c>,
/// <c>GcRecord</c> for soft-delete/invalidation.
/// </summary>
public class MfaLoginChallenge : BaseAuditableEntity
{
    /// <summary>
    /// Initializes a new instance with a generated identifier.
    /// </summary>
    public MfaLoginChallenge() { }

    /// <summary>
    /// Initializes a new instance with the supplied identifier. Used by callers that
    /// need a stable Id before persistence.
    /// </summary>
    public MfaLoginChallenge(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Foreign key to <see cref="ApplicationUser"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Same as <c>LoginCommand.TargetCompanyId</c> — re-evaluated on <c>verify</c>, never cached here.
    /// </summary>
    public Guid? TargetCompanyId { get; set; }

    /// <summary>
    /// PBKDF2 hash of the opaque token returned to the client as <c>ChallengeToken</c>. Never stored in clear text.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant after which the challenge is no longer valid (issued + <c>Mfa:ChallengeExpirationMinutes</c>).
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC instant the challenge was consumed by a successful <c>verify</c>. Null until then.
    /// </summary>
    public DateTime? ConsumedAtUtc { get; set; }

    /// <summary>
    /// Attempt counter across both methods — incremented on every failed <c>verify</c>. The whole
    /// challenge is invalidated once the count crosses the spec'd <c>5</c> threshold.
    /// </summary>
    public int AttemptCount { get; set; }

    // --- Email method sub-state (one challenge = one active code, no child table needed) ---

    /// <summary>
    /// PBKDF2 hash of the 6-digit code last sent by email for this challenge. Null until a
    /// <c>send</c> succeeds.
    /// </summary>
    public string? EmailCodeHash { get; set; }

    /// <summary>
    /// UTC instant after which <see cref="EmailCodeHash"/> is no longer valid.
    /// </summary>
    public DateTime? EmailCodeExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC instant the email code was last sent. Drives the 60s resend cooldown on <c>send</c>.
    /// </summary>
    public DateTime? EmailSentAtUtc { get; set; }

    /// <summary>
    /// Navigation property.
    /// </summary>
    public virtual ApplicationUser? User { get; set; }
}
