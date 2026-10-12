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
/// SPEC 43-post — the sidebar of a non-SuperAdmin only shows options of modules that are active for its company,
/// the SuperAdmin menu is never filtered, only the SuperAdmin enables or disables a company's modules, and the
/// change is visible right away (cache invalidated). Runs on <see cref="PostgreSqlWebApplicationFactory"/> with the
/// development seed (JOIN-001, manager@join.com, lcano@join.com, livingstone23@gmail.com).
/// </summary>
public sealed class SidebarMenuPostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private const string Password = "ABCabc123*";
    private readonly PostgreSqlWebApplicationFactory _factory;

    public SidebarMenuPostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DisablingAModule_HidesItFromNonSuperAdminMenuRightAway_AndReenablingBringsItBack()
    {
        using var manager = await LoginAsync("manager@join.com");
        using var superAdmin = await LoginAsync("lcano@join.com");
        var ticketsAssignmentId = await JoinCompanyModuleIdAsync("Tickets");

        var before = await SidebarNamesAsync(manager);
        before.Should().Contain(["TicketManagement", "Tickets"], "Tickets is a base module assigned to JOIN-001");
        (await SidebarNamesAsync(superAdmin)).Should().Contain("TimeUnits");

        try
        {
            var disable = await superAdmin.PutAsJsonAsync($"/api/v1/CompanyModules/{ticketsAssignmentId}", new { isActive = false });
            disable.StatusCode.Should().Be(HttpStatusCode.OK, await disable.Content.ReadAsStringAsync());

            var after = await SidebarNamesAsync(manager);
            after.Should().NotContain(["TicketManagement", "Tickets", "TimeUnits", "TicketStatuses"],
                "options of an inactive module are hidden and the cached menu was invalidated");
            after.Should().Contain("CustomersMenu", "other modules are not affected");

            (await SidebarNamesAsync(superAdmin)).Should().Contain("TimeUnits", "the SuperAdmin menu is never filtered (A8)");
        }
        finally
        {
            var enable = await superAdmin.PutAsJsonAsync($"/api/v1/CompanyModules/{ticketsAssignmentId}", new { isActive = true });
            enable.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await SidebarNamesAsync(manager)).Should().Contain(["TicketManagement", "Tickets"], "reactivating restores the menu (A9)");
    }

    [Fact]
    public async Task OnlySuperAdmin_ChangesCompanyModules_SuperAdminCompanyOnlyReadsItsOwn()
    {
        using var superAdminCompany = await LoginAsync("livingstone23@gmail.com");
        var ticketsAssignmentId = await JoinCompanyModuleIdAsync("Tickets");

        var update = await superAdminCompany.PutAsJsonAsync($"/api/v1/CompanyModules/{ticketsAssignmentId}", new { isActive = false });
        update.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await superAdminCompany.GetAsync("/api/v1/CompanyModules?pageNumber=1&pageSize=100");
        var body = await list.Content.ReadAsStringAsync();
        list.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var document = JsonDocument.Parse(body);
        var items = document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        var joinCompanyId = await JoinCompanyIdAsync();
        items.Should().NotBeEmpty();
        items.Should().OnlyContain(item => item.GetProperty("companyId").GetGuid() == joinCompanyId);
        items.Should().OnlyContain(item => item.GetProperty("isBase").GetBoolean(), "the seed only assigns base modules");
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", payload.Data.CompanyId.ToString());
        return client;
    }

    private static async Task<IReadOnlyList<string>> SidebarNamesAsync(HttpClient client)
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

    private async Task<Guid> JoinCompanyIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Companies.IgnoreQueryFilters().Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();
    }

    private async Task<Guid> JoinCompanyModuleIdAsync(string moduleName)
    {
        var companyId = await JoinCompanyIdAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await (from cm in db.CompanyModules.IgnoreQueryFilters()
                      join sm in db.SystemModules.IgnoreQueryFilters() on cm.ModuleId equals sm.Id
                      where cm.CompanyId == companyId && sm.Name == moduleName && cm.GcRecord == 0
                      select cm.Id).SingleAsync();
    }
}
