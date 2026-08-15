using System.Security.Claims;
using JOIN.Application.Interface;



namespace JOIN.Services.WebApi.Services;



/// <summary>
/// Implementation of ICurrentUserService that extracts the user's identity 
/// and tenant context from the active HTTP request (Headers or JWT claims).
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurrentUserService"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Provides access to the current HttpContext.</param>
    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Extracts the UserId from the 'NameIdentifier' claim inside the JWT.
    /// </summary>
    public string? UserId => _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    /// <summary>
    /// Validates if the current user identity is marked as authenticated by the ASP.NET Core pipeline.
    /// </summary>
    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Resolves the CompanyId (Tenant) for the current request.
    /// For authenticated requests, the value must come from the JWT claim.
    /// The header fallback remains available only for unauthenticated local development scenarios.
    /// </summary>
    public Guid CompanyId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return Guid.Empty;

            var isAuthenticated = httpContext.User?.Identity?.IsAuthenticated ?? false;

            // 1. Authenticated requests must resolve the tenant from JWT claims.
            var companyClaim = httpContext.User?.FindFirstValue("CompanyId");
            if (Guid.TryParse(companyClaim, out var claimCompanyId))
            {
                return claimCompanyId;
            }

            if (isAuthenticated)
            {
                return Guid.Empty;
            }

            // 2. Optional fallback for local development/testing without authentication.
            var headerValue = httpContext.Request.Headers["X-Company-Id"].FirstOrDefault();
            if (!string.IsNullOrEmpty(headerValue) && Guid.TryParse(headerValue, out var headerCompanyId))
            {
                return headerCompanyId;
            }

            return Guid.Empty;
        }
    }

    /// <summary>
    /// Reads the <c>refresh_token_id</c> claim from the current JWT and parses it as a <see cref="Guid"/>.
    /// Returns <c>null</c> when the claim is absent or unparseable (back-compat for tokens
    /// issued before the claim was introduced).
    /// </summary>
    public Guid? RefreshTokenId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.User?.FindFirstValue("refresh_token_id");
            return Guid.TryParse(raw, out var parsedId) ? parsedId : null;
        }
    }

    /// <summary>
    /// Resolves the originating client IP address.
    /// Order: first entry of <c>X-Forwarded-For</c>, then <c>Connection.RemoteIpAddress</c>.
    /// </summary>
    public string? IpAddress
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            var forwarded = httpContext.Request?.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var firstHop = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstHop))
                {
                    return firstHop;
                }
            }

            return httpContext.Connection?.RemoteIpAddress?.ToString();
        }
    }

    /// <summary>
    /// Reads the originating client User-Agent header (raw). Returns <c>null</c> when missing.
    /// </summary>
    public string? UserAgent => _httpContextAccessor.HttpContext?.Request?.Headers.UserAgent.FirstOrDefault();
}