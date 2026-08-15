using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// One-shot SMS verification code stored under <c>Security.PhoneVerificationCodes</c>.
/// PII (the raw phone in E.164) lives next to a PBKDF2 hash of the 6-digit numeric code.
/// Soft-delete follows the project's <c>GcRecord</c> convention.
/// </summary>
public class PhoneVerificationCode : BaseAuditableEntity
{
    /// <summary>
    /// Initializes a new instance with a generated identifier.
    /// </summary>
    public PhoneVerificationCode() { }

    /// <summary>
    /// Initializes a new instance with the supplied identifier. Used by callers that
    /// need a stable Id before persistence.
    /// </summary>
    public PhoneVerificationCode(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Foreign key to <see cref="ApplicationUser"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Phone number in E.164 format the user requested verification for.
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// PBKDF2-SHA256 hash of the 6-digit code; stored as <c>base64(salt):base64(hash)</c>.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant after which the code is no longer valid (issued + 10 minutes).
    /// </summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// UTC instant the code was consumed. Null until used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// Attempt counter — incremented on every failed confirm. Code is invalidated once
    /// the count crosses the spec'd <c>5</c> threshold.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Navigation property.
    /// </summary>
    public virtual ApplicationUser? User { get; set; }
}
