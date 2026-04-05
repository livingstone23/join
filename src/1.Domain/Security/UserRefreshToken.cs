using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Represents a persisted refresh token issued for an authenticated user session.
/// </summary>
public class UserRefreshToken : BaseAuditableEntity
{
    /// <summary>
    /// Gets or sets the identifier of the user who owns the refresh token.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the raw refresh token value.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC expiration date of the refresh token.
    /// </summary>
    public DateTime ExpiryDate { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the refresh token has been revoked.
    /// </summary>
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Gets or sets the navigation reference to the owner user.
    /// </summary>
    public virtual ApplicationUser User { get; set; } = null!;
}
