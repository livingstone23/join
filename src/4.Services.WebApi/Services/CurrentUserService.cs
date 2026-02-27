using System.Security.Claims;
using JOIN.Application.Interface;



namespace JOIN.Services.WebApi.Services;



/// <summary>
/// Implementation of ICurrentUserService that extracts the user's identity 
/// from the active HTTP request context (specifically from the JWT claims).
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
}