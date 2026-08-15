using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// One-time recovery code a user can use to bypass TOTP after losing the authenticator device.
/// Stored as a PBKDF2-SHA256 hash (see <see cref="Infrastructure.Security.Mfa.RecoveryCodeHasher"/>).
/// Once used (<c>UsedAtUtc</c> set) the row sticks around for audit; recovery code generation
/// replaces the whole set via <c>DeleteAllByUserAsync</c>.
/// </summary>
public class UserMfaRecoveryCode : BaseAuditableEntity
{
    /// <summary>
    /// Initializes a new instance with a generated identifier.
    /// </summary>
    public UserMfaRecoveryCode() { }

    /// <summary>
    /// Initializes a new instance with the supplied identifier. Used by tests and by callers
    /// that need a stable Id before persistence.
    /// </summary>
    public UserMfaRecoveryCode(Guid id)
    {
        Id = id;
    }

    /// <summary>
    /// Foreign key to <see cref="ApplicationUser"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// PBKDF2-SHA256 hash, base64-encoded as <c>salt:hash</c>.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// UTC instant the recovery code was minted.
    /// </summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// UTC instant the recovery code was consumed. Null until used.
    /// </summary>
    public DateTime? UsedAtUtc { get; set; }

    /// <summary>
    /// Navigation property.
    /// </summary>
    public virtual ApplicationUser? User { get; set; }
}
