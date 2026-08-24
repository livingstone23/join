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
    /// Nullable so the relationship stays optional — <see cref="ApplicationUser"/> carries
    /// a global query filter (<c>GcRecord == 0 &amp;&amp; IsActive</c>) which EF would otherwise
    /// warn about on a required principal end. Recovery codes persist for audit when a user
    /// is soft-deleted or deactivated; cascade delete still cleans them up on hard delete.
    /// </summary>
    public Guid? UserId { get; set; }

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
