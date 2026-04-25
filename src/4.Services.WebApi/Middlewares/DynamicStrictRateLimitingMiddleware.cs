using JOIN.Services.WebApi.Services.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace JOIN.Services.WebApi.Middlewares;

/// <summary>
/// Middleware that dynamically applies the "Strict" rate limiting policy to protected endpoints.
/// Executes before the rate limiting middleware to set the correct policy based on configuration.
/// Uses IOptionsSnapshot for hot-reload without API restart.
/// </summary>
public sealed class DynamicStrictRateLimitingMiddleware(RequestDelegate next, ILogger<DynamicStrictRateLimitingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<DynamicStrictRateLimitingMiddleware> _logger = logger;

    /// <summary>
    /// Invokes the middleware to check if strict rate limiting should be applied to the current request.
    /// </summary>
    /// <param name="httpContext">The HTTP context.</param>
    /// <param name="options">The rate limiting options snapshot.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InvokeAsync(HttpContext httpContext, IOptionsSnapshot<JoinRateLimitingOptions> options)
    {
        if (options.Value.EnableStrictSecurity)
        {
            // Mark protected endpoints for strict rate limiting
            var path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            if (path.Contains("/users/login", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/users/refresh", StringComparison.OrdinalIgnoreCase))
            {
                httpContext.Items["ApplyStrictRateLimit"] = true;
                _logger.LogDebug("Strict rate limiting will be applied to endpoint: {Path}", path);
            }
        }

        await _next(httpContext);
    }
}
