// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 51-post — <c>POST /Users/sidebar/cache/invalidate</c> (and its alias <c>/Users/cleancache</c>) removes the
/// real permission snapshot (<c>permissions:v2:{companyId}:{userId}</c>, written by <c>PermissionService</c>) for
/// <c>permission</c>/<c>permissions</c>/<c>all</c>, so the user's next request rebuilds it from the database, while
/// <c>sidebar</c> only clears the menu cache. An unsupported key never reaches the handler over HTTP:
/// <c>CleanCacheCommandValidator</c> rejects it first with 400 (the handler's <c>INVALID_CACHE_KEY</c> is a defensive
/// fallback covered by the unit tests). Development seed: JOIN-001, lcano@join.com (SuperAdmin), manager@join.com.
/// </summary>
public sealed class CleanCachePostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "ABCabc123*";
    private const string ProtectedEndpoint = "/api/v1/Persons?pageNumber=1&pageSize=1";
    private readonly CustomWebApplicationFactory _factory;

    public CleanCachePostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/Users/sidebar/cache/invalidate", "permission")]
    [InlineData("/api/v1/Users/sidebar/cache/invalidate", "permissions")]
    [InlineData("/api/v1/Users/cleancache", "all")]
    public async Task InvalidatingPermissions_RemovesTheV2Snapshot_AndTheNextRequestRebuildsIt(string route, string cacheKey)
    {
        var (manager, managerLogin) = await LoginAsync("manager@join.com");
        using var managerDisposal = manager;
        var (superAdmin, _) = await LoginAsync("lcano@join.com");
        using var superAdminDisposal = superAdmin;
        var cache = _factory.Services.GetRequiredService<IMemoryCache>();
        var permissionKey = $"permissions:v2:{managerLogin.CompanyId}:{managerLogin.UserId}";
        var sidebarKey = $"sidebar:{managerLogin.CompanyId}:{managerLogin.UserId}";

        await manager.GetAsync(ProtectedEndpoint);
        (await manager.GetAsync("/api/v1/Users/sidebar")).StatusCode.Should().Be(HttpStatusCode.OK);
        cache.TryGetValue(permissionKey, out _).Should().BeTrue("the dynamic authorization filter caches the snapshot");
        cache.TryGetValue(sidebarKey, out _).Should().BeTrue("the sidebar query caches the menu");

        var invalidate = await superAdmin.PostAsJsonAsync(route, new
        {
            userId = managerLogin.UserId,
            companyId = managerLogin.CompanyId,
            cacheKey
        });

        invalidate.StatusCode.Should().Be(HttpStatusCode.OK, await invalidate.Content.ReadAsStringAsync());
        cache.TryGetValue(permissionKey, out _).Should().BeFalse("the endpoint must remove the real permissions:v2 key");
        cache.TryGetValue(sidebarKey, out _).Should().BeFalse("the menu depends on the permissions and is cleared too");

        await manager.GetAsync(ProtectedEndpoint);
        cache.TryGetValue(permissionKey, out _).Should().BeTrue("the next request rebuilds the snapshot from the database");
    }

    [Fact]
    public async Task InvalidatingSidebar_KeepsThePermissionSnapshot()
    {
        var (manager, managerLogin) = await LoginAsync("manager@join.com");
        using var managerDisposal = manager;
        var (superAdmin, _) = await LoginAsync("lcano@join.com");
        using var superAdminDisposal = superAdmin;
        var cache = _factory.Services.GetRequiredService<IMemoryCache>();
        var permissionKey = $"permissions:v2:{managerLogin.CompanyId}:{managerLogin.UserId}";
        var sidebarKey = $"sidebar:{managerLogin.CompanyId}:{managerLogin.UserId}";

        await manager.GetAsync(ProtectedEndpoint);
        (await manager.GetAsync("/api/v1/Users/sidebar")).StatusCode.Should().Be(HttpStatusCode.OK);

        var invalidate = await superAdmin.PostAsJsonAsync("/api/v1/Users/sidebar/cache/invalidate", new
        {
            userId = managerLogin.UserId,
            companyId = managerLogin.CompanyId,
            cacheKey = "sidebar"
        });

        invalidate.StatusCode.Should().Be(HttpStatusCode.OK, await invalidate.Content.ReadAsStringAsync());
        cache.TryGetValue(sidebarKey, out _).Should().BeFalse();
        cache.TryGetValue(permissionKey, out _).Should().BeTrue("'sidebar' only clears the menu cache");
    }

    [Fact]
    public async Task UnsupportedCacheKey_IsRejectedByTheValidatorWithBadRequest()
    {
        var (superAdmin, superAdminLogin) = await LoginAsync("lcano@join.com");
        using var superAdminDisposal = superAdmin;

        var invalidate = await superAdmin.PostAsJsonAsync("/api/v1/Users/sidebar/cache/invalidate", new
        {
            userId = superAdminLogin.UserId,
            companyId = superAdminLogin.CompanyId,
            cacheKey = "audit-log"
        });
        var body = await invalidate.Content.ReadAsStringAsync();

        invalidate.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("CacheKey must be one of");
    }

    private async Task<(HttpClient Client, LoginResponse Login)> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = (await login.Content.ReadFromJsonAsync<Response<LoginResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", payload.CompanyId.ToString());
        return (client, payload);
    }
}
