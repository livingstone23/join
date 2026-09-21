using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// One-shot code for the authenticated <c>email-otp/send-code</c> → <c>enable</c>/<c>disable</c> flow.
/// Exact clone of <see cref="PhoneVerificationCode"/>'s shape, but for email. Not to be confused with
/// <see cref="MfaLoginChallenge.EmailCodeHash"/>, which is the pre-JWT login-challenge code.
/// </summary>
public class EmailOtpEnableCode : BaseAuditableEntity
{
    /// <summary>
    /// Initializes a new instance with a generated identifier.
    /// </summary>
    public EmailOtpEnableCode() { }

    /// <summary>
    /// Initializes a new instance with the supplied identifier. Used by callers that
    /// need a stable Id before persistence.
    /// </summary>
    public EmailOtpEnableCode(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Foreign key to <see cref="ApplicationUser"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Confirmed email address the code was sent to.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2-SHA256 hash of the 6-digit code; stored as <c>base64(salt):base64(hash)</c>.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant after which the code is no longer valid.
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC instant the code was consumed by <c>enable</c>/<c>disable</c>. Null until used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// Attempt counter — incremented on every failed <c>enable</c>/<c>disable</c>. Code is invalidated
    /// once the count crosses the spec'd <c>5</c> threshold.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Navigation property.
    /// </summary>
    public virtual ApplicationUser? User { get; set; }
}
