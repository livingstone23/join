// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 50 — user-administration endpoints whose Dapper SQL failed on PostgreSQL and were not covered
/// by any test: list parameters (<c>IN @list</c> → <c>= ANY(@list)</c>, because Dapper hands lists to
/// Npgsql as arrays instead of expanding them) and optional filters whose <c>@p IS NULL</c> check
/// reached the server untyped (42P08). Development seed: JOIN-001, lcano@join.com (SuperAdmin),
/// manager@join.com.
/// </summary>
public sealed class UserAdministrationPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public UserAdministrationPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/v1/Users/reports/system")]
    [InlineData("/api/v1/Users/reports/system?roleNames=Manager&roleNames=supervisor&fromDate=2020-01-01&toDate=2100-01-01")]
    [InlineData("/api/v1/Users/reports/my-company")]
    [InlineData("/api/v1/Users/reports/my-company?roleNames=Manager&search=MANAGER&isActive=true&fromDate=2020-01-01&toDate=2100-01-01")]
    [InlineData("/api/v1/Audit/security")]
    [InlineData("/api/v1/Audit/security?entity=Role&action=Updated&fromDate=2020-01-01&toDate=2100-01-01")]
    public async Task ReportsAndAudit_WithAndWithoutOptionalFilters_ReturnOk(string url)
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");

        var response = await superAdmin.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UserReport_FilteredByRoleName_ReturnsOnlyUsersWithThatRole()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");

        var response = await superAdmin.GetAsync("/api/v1/Users/reports/system?roleNames=manager");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("manager@join.com").And.NotContain("simpleuser@join.com");
    }

    [Fact]
    public async Task ReplaceAndBulkUpdateUserRoles_ResolveRoleListsOnPostgreSql()
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(_factory, "lcano@join.com");
        Guid managerId;
        Guid supervisorRoleId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            managerId = await db.ApplicationUsers.IgnoreQueryFilters()
                .Where(u => u.Email == "manager@join.com").Select(u => u.Id).SingleAsync();
            supervisorRoleId = await db.ApplicationRoles.IgnoreQueryFilters()
                .Where(r => r.Name == "Supervisor").Select(r => r.Id).SingleAsync();
            var companyId = await db.Companies.IgnoreQueryFilters()
                .Where(c => c.TaxId == "JOIN-001").Select(c => c.Id).SingleAsync();

            // The seed does not link roles to JOIN-001 in RoleCompanies; ReplaceUserRoles only
            // resolves roles linked to the caller's tenant.
            var roleIds = await db.ApplicationRoles.IgnoreQueryFilters()
                .Where(r => r.Name == "Manager" || r.Name == "Supervisor").Select(r => r.Id).ToListAsync();
            var linked = await db.RoleCompanies.IgnoreQueryFilters()
                .Where(rc => rc.CompanyId == companyId && rc.GcRecord == 0).Select(rc => rc.RoleId).ToListAsync();
            db.RoleCompanies.AddRange(roleIds.Except(linked).Select(roleId => new RoleCompany
            {
                RoleId = roleId,
                CompanyId = companyId,
                Created = DateTime.UtcNow,
                CreatedBy = nameof(UserAdministrationPostgreSqlTests)
            }));
            await db.SaveChangesAsync();
        }

        var replace = await superAdmin.PutAsJsonAsync($"/api/v1/users/{managerId}/roles", new { roles = new[] { "Manager", "supervisor" } });
        replace.StatusCode.Should().Be(HttpStatusCode.OK, await replace.Content.ReadAsStringAsync());
        (await replace.Content.ReadAsStringAsync()).Should().Contain("Supervisor");

        var bulk = await superAdmin.PutAsJsonAsync("/api/v1/users/roles/bulk", new
        {
            userIds = new[] { managerId },
            addRoleIds = Array.Empty<Guid>(),
            removeRoleIds = new[] { supervisorRoleId }
        });
        bulk.StatusCode.Should().Be(HttpStatusCode.OK, await bulk.Content.ReadAsStringAsync());
    }
}
