using JOIN.Domain.Audit;



namespace JOIN.Domain.Security;



/// <summary>
/// Represents a persisted refresh token issued for an authenticated user session.
/// </summary>
public class UserRefreshToken : BaseAuditableEntity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserRefreshToken"/> class with a generated identifier.
    /// </summary>
    public UserRefreshToken() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="UserRefreshToken"/> class with the supplied identifier.
    /// Used when the caller needs to embed the identifier inside the access token (e.g. the
    /// <c>refresh_token_id</c> claim) before persistence and therefore must pre-generate the row id.
    /// </summary>
    /// <param name="id">The pre-determined identifier for the new row.</param>
    public UserRefreshToken(Guid id)
    {
        Id = id;
    }

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
