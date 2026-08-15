


namespace JOIN.Application.Interface;



/// <summary>
/// Provides access to the currently authenticated user's context.
/// Essential for auditing, authorization, and multi-tenant data filtering
/// without coupling the Application or Domain layers to HTTP specifics.
/// </summary>
public interface ICurrentUserService
{

    /// <summary>
    /// Gets the unique identifier (GUID) of the currently authenticated user.
    /// Returns null if the request is not authenticated (e.g., anonymous endpoints or background jobs).
    /// </summary>
    string? UserId { get; }


    /// <summary>
    /// Gets the unique identifier (GUID) of the company associated with the currently authenticated user.
    /// This is essential for multi-tenant data filtering and ensuring that users can only access data within their own company context.
    /// </summary>
    /// <value></value>
    Guid CompanyId { get; }


    /// <summary>
    /// Gets a value indicating whether the current execution context is associated with an authenticated user.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the GUID identifier of the active refresh token row in <c>Security.UserRefreshTokens</c>.
    /// Resolved from the <c>refresh_token_id</c> JWT claim. Returns <c>null</c> when the claim is absent
    /// or cannot be parsed (e.g. tokens issued before the refresh-token-id claim was introduced).
    /// </summary>
    Guid? RefreshTokenId { get; }

    /// <summary>
    /// Gets the originating client IP address for the current request.
    /// Resolution order: <c>X-Forwarded-For</c> header (first hop), then <c>Connection.RemoteIpAddress</c>.
    /// Returns <c>null</c> when the value cannot be resolved.
    /// </summary>
    string? IpAddress { get; }

    /// <summary>
    /// Gets the originating client User-Agent header for the current request.
    /// Returns <c>null</c> when the header is absent.
    /// </summary>
    string? UserAgent { get; }

}