// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 48-post — with <c>Modules:EnforceCompanyModules = true</c>, a non-SuperAdmin cannot call the APIs of a module
/// that is not active for its company (<c>403 MODULE_NOT_ENABLED</c>), reactivating it works without a restart
/// (cache invalidation), the SuperAdmin is never blocked, and <c>GET /CompanyModules/diagnostics</c> reports the
/// company and is SuperAdmin-only. Development seed: JOIN-001, manager@join.com, lcano@join.com (SuperAdmin).
/// </summary>
public sealed class ModuleEnforcementPostgreSqlTests : IClassFixture<ModuleEnforcementPostgreSqlWebApplicationFactory>
{
    private readonly ModuleEnforcementPostgreSqlWebApplicationFactory _factory;

    public ModuleEnforcementPostgreSqlTests(ModuleEnforcementPostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DisabledModule_Returns403ModuleNotEnabled_AndReenablingRestoresAccessWithoutRestart()
    {
        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var ticketsAssignmentId = await ModuleTestHelpers.JoinCompanyModuleIdAsync(_factory, "Tickets");

        // Prime the module cache with the module enabled, so the 403 below proves invalidation.
        (await manager.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: false);

            var blocked = await manager.GetAsync("/api/v1/Tickets");
            var body = await blocked.Content.ReadAsStringAsync();
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
            using (var problem = JsonDocument.Parse(body))
            {
                problem.RootElement.GetProperty("code").GetString().Should().Be("MODULE_NOT_ENABLED");
                problem.RootElement.GetProperty("module").GetString().Should().Be("Tickets");
                problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue("the global ProblemDetails setup applies (C)");
                problem.RootElement.GetProperty("instance").GetString().Should().Be("/api/v1/Tickets");
            }

            (await superAdmin.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK,
                "the SuperAdmin is never blocked by module (B2)");
            (await manager.GetAsync("/api/v1/Persons?pageNumber=1&pageSize=5")).StatusCode.Should().Be(HttpStatusCode.OK,
                "other modules are not affected");
        }
        finally
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: true);
        }

        (await manager.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK,
            "reactivating the module invalidates the per-company module cache");
    }

    /// <summary>
    /// A module inactive globally (<c>SystemModule.IsActive = false</c>) blocks every company, and the change made
    /// through <c>PUT /SystemModules</c> reaches the module cache built by another request (global version token).
    /// </summary>
    [Fact]
    public async Task GloballyInactiveModule_Returns403_AndReactivatingRestoresAccessWithoutRestart()
    {
        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var module = await ModuleTestHelpers.SystemModuleAsync(_factory, "Tickets");

        (await manager.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            await SetSystemModuleActiveAsync(superAdmin, module, isActive: false);

            var blocked = await manager.GetAsync("/api/v1/Tickets");
            var body = await blocked.Content.ReadAsStringAsync();
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, body);
            using var problem = JsonDocument.Parse(body);
            problem.RootElement.GetProperty("code").GetString().Should().Be("MODULE_NOT_ENABLED");

            (await superAdmin.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await SetSystemModuleActiveAsync(superAdmin, module, isActive: true);
        }

        (await manager.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK,
            "reactivating the module cancels the global module-cache token");
    }

    private static async Task SetSystemModuleActiveAsync(HttpClient superAdmin, Domain.Admin.SystemModule module, bool isActive)
    {
        var response = await superAdmin.PutAsJsonAsync($"/api/v1/SystemModules/{module.Id}", new
        {
            name = module.Name,
            description = module.Description,
            icon = module.Icon,
            isActive,
            isBase = module.IsBase,
            order = module.Order
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Diagnostics_ListsCompanyWithMissingBaseModule_AndIsSuperAdminOnly()
    {
        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var joinCompanyId = await ModuleTestHelpers.JoinCompanyIdAsync(_factory);
        var ticketsAssignmentId = await ModuleTestHelpers.JoinCompanyModuleIdAsync(_factory, "Tickets");

        (await manager.GetAsync("/api/v1/CompanyModules/diagnostics")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var complete = await DiagnosticsAsync(superAdmin);
        complete.GetProperty("enforceCompanyModules").GetBoolean().Should().BeTrue();
        complete.GetProperty("baseModules").EnumerateArray().Select(m => m.GetString()).Should().Contain("Tickets");
        complete.GetProperty("companies").EnumerateArray()
            .Should().NotContain(c => c.GetProperty("companyId").GetGuid() == joinCompanyId, "JOIN-001 has every base module");

        try
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: false);

            var incomplete = await DiagnosticsAsync(superAdmin);
            var entry = incomplete.GetProperty("companies").EnumerateArray()
                .Single(c => c.GetProperty("companyId").GetGuid() == joinCompanyId);
            entry.GetProperty("missingBaseModules").EnumerateArray().Select(m => m.GetString()).Should().Equal("Tickets");
            entry.GetProperty("usersWithPermissionsInDisabledModules").GetInt32().Should().BeGreaterThan(0,
                "the seed grants ticket permissions to JOIN-001 roles");
        }
        finally
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: true);
        }
    }

    private static async Task<JsonElement> DiagnosticsAsync(HttpClient superAdmin)
    {
        var response = await superAdmin.GetAsync("/api/v1/CompanyModules/diagnostics");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}

/// <summary>
/// SPEC 48-post — with the switch off (deployment default), <c>CompanyModules</c> is ignored for menu and APIs (B4/B5):
/// a disabled module still answers and still shows in the menu.
/// </summary>
public sealed class ModuleEnforcementSwitchOffPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ModuleEnforcementSwitchOffPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DisabledModule_StillAnswersAndShowsInMenu_WhenSwitchIsOff()
    {
        using var manager = await ModuleTestHelpers.LoginAsync(_factory, "manager@join.com");
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        var ticketsAssignmentId = await ModuleTestHelpers.JoinCompanyModuleIdAsync(_factory, "Tickets");

        try
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: false);

            (await manager.GetAsync("/api/v1/Tickets")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await ModuleTestHelpers.SidebarNamesAsync(manager)).Should().Contain(["TicketManagement", "Tickets"]);
        }
        finally
        {
            await ModuleTestHelpers.SetCompanyModuleActiveAsync(superAdmin, ticketsAssignmentId, isActive: true);
        }
    }
}

/// <summary>
/// Shared HTTP and database helpers for the SPEC 48-post module tests.
/// </summary>
internal static class ModuleTestHelpers
{
    private const string Password = "ABCabc123*";

    public static async Task<HttpClient> LoginAsync(CustomWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", payload.Data.CompanyId.ToString());
        return client;
    }

    public static async Task SetCompanyModuleActiveAsync(HttpClient superAdmin, Guid assignmentId, bool isActive)
    {
        var response = await superAdmin.PutAsJsonAsync($"/api/v1/CompanyModules/{assignmentId}", new { isActive });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<IReadOnlyList<string>> SidebarNamesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/Users/sidebar");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        var names = new List<string>();
        Collect(document.RootElement.GetProperty("data"), names);
        return names;

        static void Collect(JsonElement nodes, List<string> names)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                names.Add(node.GetProperty("name").GetString()!);
                Collect(node.GetProperty("children"), names);
            }
        }
    }

    public static async Task<Guid> JoinCompanyIdAsync(CustomWebApplicationFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
    }

    public static async Task<Domain.Admin.SystemModule> SystemModuleAsync(CustomWebApplicationFactory factory, string moduleName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SystemModules.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Name == moduleName && m.GcRecord == 0);
    }

    public static async Task<Guid> JoinCompanyModuleIdAsync(CustomWebApplicationFactory factory, string moduleName)
    {
        var companyId = await JoinCompanyIdAsync(factory);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await (from cm in db.CompanyModules.IgnoreQueryFilters()
                      join sm in db.SystemModules.IgnoreQueryFilters() on cm.ModuleId equals sm.Id
                      where cm.CompanyId == companyId && sm.Name == moduleName && cm.GcRecord == 0
                      select cm.Id).SingleAsync();
    }
}
