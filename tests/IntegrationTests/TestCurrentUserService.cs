// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Security.Claims;
using JOIN.Application.Interface;
using Microsoft.AspNetCore.Http;

namespace JOIN.IntegrationTests;

/// <summary>
/// Test double for <see cref="ICurrentUserService"/> that resolves tenant/user identity
/// from the active <see cref="HttpContext"/> when one is present (matches the production
/// <c>CurrentUserService</c> behavior — JWT claim <c>CompanyId</c> for authenticated
/// requests, <c>X-Company-Id</c> header for unauthenticated local/test traffic) and
/// falls back to an explicit per-scope override when the scope is created outside of
/// an HTTP pipeline (the EF-only test scopes created by
/// <see cref="Persistence.GlobalQueryFiltersIntegrationTests.CreateScopeAsCompany"/>
/// have no <c>HttpContext</c>, so without this override the global query filters
/// would evaluate <c>CompanyId == Guid.Empty</c> and hide every tenant-scoped row).
/// </summary>
public sealed class TestCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _accessor;
    private Guid _companyIdOverride;

    public TestCurrentUserService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    /// <summary>
    /// Per-scope override used by EF-only test scopes. Ignored when an HTTP context
    /// is present (HTTP path keeps production semantics). Cleared between tests by
    /// recreating the scope.
    /// </summary>
    public Guid CompanyIdOverride
    {
        get => _companyIdOverride;
        set => _companyIdOverride = value;
    }

    public string? UserId =>
        _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public bool IsAuthenticated =>
        _accessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public Guid CompanyId
    {
        get
        {
            var httpContext = _accessor.HttpContext;
            if (httpContext != null)
            {
                var isAuthenticated = httpContext.User?.Identity?.IsAuthenticated ?? false;

                var companyClaim = httpContext.User?.FindFirstValue("CompanyId");
                if (Guid.TryParse(companyClaim, out var claimCompanyId))
                {
                    return claimCompanyId;
                }

                if (isAuthenticated)
                {
                    return Guid.Empty;
                }

                var headerValue = httpContext.Request.Headers["X-Company-Id"].FirstOrDefault();
                if (!string.IsNullOrEmpty(headerValue) && Guid.TryParse(headerValue, out var headerCompanyId))
                {
                    return headerCompanyId;
                }
            }

            return _companyIdOverride;
        }
    }

    public Guid? RefreshTokenId
    {
        get
        {
            var raw = _accessor.HttpContext?.User?.FindFirstValue("refresh_token_id");
            return Guid.TryParse(raw, out var parsedId) ? parsedId : null;
        }
    }

    public string? IpAddress
    {
        get
        {
            var httpContext = _accessor.HttpContext;
            if (httpContext == null)
            {
                return null;
            }

            var forwarded = httpContext.Request?.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var firstHop = forwarded
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstHop))
                {
                    return firstHop;
                }
            }

            return httpContext.Connection?.RemoteIpAddress?.ToString();
        }
    }

    public string? UserAgent =>
        _accessor.HttpContext?.Request?.Headers.UserAgent.FirstOrDefault();

    public bool IsInRole(string role) =>
        _accessor.HttpContext?.User?.IsInRole(role) ?? false;
}