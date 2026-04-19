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
}