// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Persistence;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 43-post — a database with the PREVIOUS menu (Spanish names, mixed routes, every option under
/// "Administracion", no IsBase) goes through the seed and ends with the English names and hyphenated routes,
/// with the same Ids, no duplicates, the role permissions untouched and each option under its real module.
/// Runs on <see cref="PostgreSqlWebApplicationFactory"/> (<c>postgres:17</c>).
/// </summary>
public sealed class SystemMenuSeedPostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    // New option name → (previous name, previous route). Options not listed kept their name; their previous
    // route is derived from the module prefix below.
    private static readonly Dictionary<string, (string Name, string Route)> PreviousOptions = new()
    {
        ["Administration"] = ("Administracion", "/administracion"),
        ["Countries"] = ("Paises", "/administracion/countries"),
        ["StreetTypes"] = ("StreetTypes", "/administracion/Street-types"),
        ["Genders"] = ("Genero", "/administracion/gender"),
        ["Industries"] = ("Industria", "/administracion/industry"),
        ["IncomeRanges"] = ("Rango Ingreso", "/administracion/incomerange"),
        ["TaxRegimes"] = ("Regimen Tributario", "/administracion/taxregime"),
        ["CustomersMenu"] = ("Persons", "/Clientes"),
        ["Persons"] = ("Clientes", "/Clientes/Cliente"),
        ["Customers"] = ("Customers", "/Clientes/customers"),
        ["TicketManagement"] = ("ManejoTickets", "/ManejoTickets"),
        ["Security"] = ("Seguridad", "/security"),
        ["Users"] = ("Usuarios", "/security/users"),
        ["Companies"] = ("Compañias", "/security/companies"),
        ["SystemOptions"] = ("SystemOption", "/security/system-options"),
        ["RoleSystemOptions"] = ("RoleSystemOption", "/security/role-system-options"),
        ["AuditLog"] = ("Bitacora", "/security/audit-log")
    };

    private static readonly Dictionary<string, string> PreviousModules = new()
    {
        ["Administration"] = "Administracion",
        ["Customers"] = "Clientes",
        ["Security"] = "Seguridad"
    };

    private static readonly Dictionary<string, string> ExpectedModuleByRoutePrefix = new()
    {
        ["/administration"] = "Administration",
        ["/customers"] = "Customers",
        ["/tickets"] = "Tickets",
        ["/security"] = "Security"
    };

    private readonly PostgreSqlWebApplicationFactory _factory;

    public SystemMenuSeedPostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_OnADatabaseWithThePreviousMenu_RenamesInPlaceWithoutDuplicatesOrLostPermissions()
    {
        _ = _factory.CreateClient(); // migrations + full seed (already with the new names)

        var snapshot = await RevertToPreviousMenuAsync();

        await ReseedMenuAsync();
        await AssertNewMenuAsync(snapshot);

        // Idempotent: a second run changes nothing.
        await ReseedMenuAsync();
        await AssertNewMenuAsync(snapshot);
    }

    private sealed record MenuSnapshot(
        Dictionary<string, Guid> ModuleIdsByNewName,
        Dictionary<Guid, string> OptionNewNamesById,
        HashSet<(Guid RoleId, Guid OptionId, Guid CompanyId, bool CanRead, bool CanCreate, bool CanUpdate, bool CanDelete)> Permissions);

    /// <summary>Puts the menu back in its pre-SPEC 43 shape and returns what must survive the seed.</summary>
    private async Task<MenuSnapshot> RevertToPreviousMenuAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var modules = await db.SystemModules.IgnoreQueryFilters().Where(m => m.GcRecord == 0).ToListAsync();
        var moduleIds = modules.ToDictionary(m => m.Name, m => m.Id);
        var options = await db.SystemOptions.IgnoreQueryFilters().Where(o => o.GcRecord == 0 && !o.Route.StartsWith("/calendar")).ToListAsync();
        var snapshot = new MenuSnapshot(
            moduleIds,
            options.ToDictionary(o => o.Id, o => o.Name),
            (await db.RoleSystemOptions.IgnoreQueryFilters().Where(r => r.GcRecord == 0).ToListAsync())
                .Select(r => (r.RoleId, r.SystemOptionId, r.CompanyId, r.CanRead, r.CanCreate, r.CanUpdate, r.CanDelete))
                .ToHashSet());

        foreach (var module in modules)
        {
            module.Name = PreviousModules.GetValueOrDefault(module.Name, module.Name);
            module.IsBase = false;
        }

        var securityParentId = options.Single(o => o.Name == "Security").Id;
        foreach (var option in options)
        {
            var (name, route) = PreviousOptions.TryGetValue(option.Name, out var previous)
                ? previous
                : (option.Name, PreviousRoute(option.Route!));
            option.ModuleId = moduleIds["Administration"]; // every option used to hang from "Administracion"
            if (option.ParentId == securityParentId)
            {
                option.ParentId = null; // security options used to sit at the menu root
            }

            option.Name = name;
            option.Route = route;
        }

        await db.SaveChangesAsync();
        return snapshot;
    }

    private static string PreviousRoute(string route) => route switch
    {
        _ when route.StartsWith("/administration/", StringComparison.Ordinal) => "/administracion/" + route["/administration/".Length..],
        _ when route.StartsWith("/tickets/", StringComparison.Ordinal) => "/ManejoTickets/" + route["/tickets/".Length..],
        _ => route
    };

    private async Task ReseedMenuAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedMenuAndPermissionsAsync(forceReseed: true);
    }

    private async Task AssertNewMenuAsync(MenuSnapshot snapshot)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // SPEC 44-post adds the optional Calendar module and its /calendar options (left untouched by the revert).
        var modules = await db.SystemModules.IgnoreQueryFilters().Where(m => m.GcRecord == 0).ToListAsync();
        modules.Select(m => m.Name).Should().BeEquivalentTo(["Administration", "Customers", "Tickets", "Calendar", "Security"]);
        modules.Should().OnlyContain(m => m.IsBase == (m.Name != "Calendar"));
        modules.ToDictionary(m => m.Name, m => m.Id).Should().BeEquivalentTo(snapshot.ModuleIdsByNewName, "modules are renamed in place");

        var options = await db.SystemOptions.IgnoreQueryFilters().Where(o => o.GcRecord == 0 && !o.Route.StartsWith("/calendar")).ToListAsync();
        options.ToDictionary(o => o.Id, o => o.Name).Should().BeEquivalentTo(snapshot.OptionNewNamesById,
            "every option keeps its Id and gets its new name; nothing is duplicated");
        options.Select(o => o.Route).Should().OnlyHaveUniqueItems();
        options.Should().OnlyContain(o => o.Route == o.Route!.ToLowerInvariant() && !o.Route.Contains('_'));

        var moduleNameById = modules.ToDictionary(m => m.Id, m => m.Name);
        foreach (var option in options)
        {
            var prefix = "/" + option.Route!.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
            moduleNameById[option.ModuleId].Should().Be(ExpectedModuleByRoutePrefix[prefix], $"{option.Name} belongs to its real module");
        }

        var securityParent = options.Single(o => o.Name == "Security");
        options.Where(o => o.Route!.StartsWith("/security/", StringComparison.Ordinal))
            .Should().OnlyContain(o => o.ParentId == securityParent.Id, "security options hang from the Security parent");

        var permissions = (await db.RoleSystemOptions.IgnoreQueryFilters().Where(r => r.GcRecord == 0).ToListAsync())
            .Select(r => (r.RoleId, r.SystemOptionId, r.CompanyId, r.CanRead, r.CanCreate, r.CanUpdate, r.CanDelete))
            .ToHashSet();
        permissions.Should().BeEquivalentTo(snapshot.Permissions, "role permissions are untouched");
    }
}
