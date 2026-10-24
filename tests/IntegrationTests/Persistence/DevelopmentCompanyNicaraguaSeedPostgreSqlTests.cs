// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Domain.Audit;
using JOIN.Persistence;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 100 (PostgreSQL, <see cref="CustomWebApplicationFactory"/> in Development) — the development company
/// <c>JOIN-NI-001</c>: NIO base with USD at 36,62, every membership of JOIN-001 copied with its roles, the same modules and
/// permissions, prices converted exactly (100 USD = 3.662,00 NIO), leads consistent with the company currency, income
/// ranges in USD, Nicaraguan geography, and an idempotent seed that leaves JOIN-001 unchanged.
/// </summary>
public sealed class DevelopmentCompanyNicaraguaSeedPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const decimal UsdRate = 36.62m;

    private readonly CustomWebApplicationFactory _factory;

    public DevelopmentCompanyNicaraguaSeedPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_ShouldCreateJoinNicaraguaWithNioBaseAndUsdRate()
    {
        _ = _factory.CreateClient(); // builds the host: migrations, full seed and the Development companies seed

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (joinId, niId) = await CompanyIdsAsync(db);

        var company = await db.Companies.IgnoreQueryFilters().SingleAsync(c => c.Id == niId);
        company.Name.Should().Be("JOIN Nicaragua");
        company.IsActive.Should().BeTrue();
        company.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);

        (await BaseCurrencyCodeAsync(db, niId)).Should().Be("NIO");
        (await BaseCurrencyCodeAsync(db, joinId)).Should().Be("USD", "JOIN-001 keeps its base");

        var usdId = await db.Currencies.IgnoreQueryFilters().Where(c => c.Code == "USD" && c.GcRecord == 0).Select(c => c.Id).SingleAsync();
        var usd = await db.CompanyCurrencies.IgnoreQueryFilters().SingleAsync(c => c.CompanyId == niId && c.CurrencyId == usdId && c.GcRecord == 0);
        usd.IsBase.Should().BeFalse();

        var rates = await db.ExchangeRates.IgnoreQueryFilters().Where(r => r.CompanyId == niId && r.GcRecord == 0).ToListAsync();
        rates.Should().ContainSingle("one rate, dated the seed day");
        rates[0].CurrencyId.Should().Be(usdId);
        rates[0].Rate.Should().Be(UsdRate, "SPEC 56: 1 USD = Rate NIO");
    }

    [Fact]
    public async Task Seed_ShouldCopyMembershipsRolesModulesAndPermissionsOfJoin001()
    {
        _ = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (joinId, niId) = await CompanyIdsAsync(db);

        var joinMembers = await db.UserCompanies.IgnoreQueryFilters().Where(uc => uc.CompanyId == joinId && uc.GcRecord == 0).ToListAsync();
        var niMembers = await db.UserCompanies.IgnoreQueryFilters().Where(uc => uc.CompanyId == niId && uc.GcRecord == 0).ToListAsync();
        joinMembers.Should().NotBeEmpty();
        niMembers.Select(m => m.UserId).Should().BeEquivalentTo(joinMembers.Select(m => m.UserId));
        niMembers.Should().OnlyContain(m => !m.IsDefault, "the default company of each user does not change");

        var livingstone = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "livingstone23@gmail.com");
        joinMembers.Single(m => m.UserId == livingstone.Id).IsDefault.Should().BeTrue();
        niMembers.Should().Contain(m => m.UserId == livingstone.Id);

        var joinRoles = await RolePairsAsync(db, joinId);
        var niRoles = await RolePairsAsync(db, niId);
        niRoles.Should().BeEquivalentTo(joinRoles, "same roles in both companies");

        var joinModules = await db.CompanyModules.IgnoreQueryFilters().Where(m => m.CompanyId == joinId && m.GcRecord == 0).Select(m => m.ModuleId).ToListAsync();
        var niModules = await db.CompanyModules.IgnoreQueryFilters().Where(m => m.CompanyId == niId && m.GcRecord == 0).Select(m => m.ModuleId).ToListAsync();
        niModules.Should().BeEquivalentTo(joinModules);
        var calendarModuleId = await db.SystemModules.IgnoreQueryFilters().Where(m => m.Name == "Calendar").Select(m => m.Id).SingleAsync();
        niModules.Should().Contain(calendarModuleId);

        var joinPermissions = await PermissionPairsAsync(db, joinId);
        var niPermissions = await PermissionPairsAsync(db, niId);
        niPermissions.Should().Contain(joinPermissions, "every permission of JOIN-001 exists in JOIN-NI-001");

        (await db.SeedStates.IgnoreQueryFilters().AnyAsync(s => s.CompanyId == niId)).Should().BeTrue();
    }

    [Fact]
    public async Task Seed_ShouldConvertAmountsExactlyAndKeepLeadsConsistent()
    {
        _ = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (joinId, niId) = await CompanyIdsAsync(db);

        var joinPrices = await PricesAsync(db, joinId);
        var niPrices = await PricesAsync(db, niId);
        niPrices.Keys.Should().BeEquivalentTo(joinPrices.Keys);
        foreach (var (code, usdPrice) in joinPrices)
        {
            niPrices[code].Should().Be(Math.Round(usdPrice * UsdRate, 2, MidpointRounding.AwayFromZero), code);
        }

        niPrices["SOP-12"].Should().Be(73240.00m, "2.000 USD = 73.240,00 NIO");
        niPrices["CRM-USR"].Should().Be(915.50m, "25 USD = 915,50 NIO");

        var currencies = await db.Currencies.IgnoreQueryFilters().Where(c => c.GcRecord == 0).ToDictionaryAsync(c => c.Id, c => c.Code);
        var leads = await db.Leads.IgnoreQueryFilters().Where(l => l.CompanyId == niId && l.GcRecord == 0).ToListAsync();
        leads.Should().NotBeEmpty();
        leads.Should().NotContain(l => currencies[l.CurrencyId] == "USD" && l.ExchangeRate == 1m, "no lead is pinned to USD with rate 1");
        leads.Where(l => currencies[l.CurrencyId] == "NIO").Should().OnlyContain(l => l.ExchangeRate == 1m);

        var manualTopics = new Dictionary<string, decimal>
        {
            ["Reventa de licencias en la zona este"] = 97043.00m, // 2.650 USD
            ["Licencias para consultorio"] = 18310.00m,           // 500 USD
            ["Caja registradora"] = 15380.40m                     // 420 USD
        };
        foreach (var (topic, value) in manualTopics)
        {
            var lead = leads.Single(l => l.Topic == topic);
            currencies[lead.CurrencyId].Should().Be("NIO", topic);
            lead.EstimatedValue.Should().Be(value, topic);
            lead.EstimatedValueBase.Should().Be(value, topic);
        }

        var usdLead = leads.Single(l => currencies[l.CurrencyId] == "USD");
        usdLead.Topic.Should().Be("Licencias y soporte");
        usdLead.ExchangeRate.Should().Be(UsdRate);
        var products = await db.Products.IgnoreQueryFilters().Where(p => p.CompanyId == niId && p.GcRecord == 0).ToDictionaryAsync(p => p.Id, p => p.Code);
        var lines = await db.LeadProducts.IgnoreQueryFilters().Where(lp => lp.LeadId == usdLead.Id && lp.GcRecord == 0).ToListAsync();
        lines.Should().NotBeEmpty();
        foreach (var line in lines)
        {
            line.UnitPrice.Should().Be(Math.Round(niPrices[products[line.ProductId]] / UsdRate, 2, MidpointRounding.AwayFromZero));
        }

        lines.Single(l => products[l.ProductId] == "SOP-12").UnitPrice.Should().Be(2000.00m, "73.240,00 NIO / 36,62 = 2.000,00 USD, never multiplied again");
        usdLead.EstimatedValue.Should().Be(lines.Sum(l => l.LineTotal));
        usdLead.EstimatedValueBase.Should().Be(Math.Round(usdLead.EstimatedValue * UsdRate, 2, MidpointRounding.AwayFromZero));

        var incomeRanges = await db.IncomeRanges.IgnoreQueryFilters().Where(r => r.CompanyId == niId && r.GcRecord == 0).ToListAsync();
        incomeRanges.Should().HaveCount(5);
        incomeRanges.Should().OnlyContain(r => r.CurrencyCode == "USD", "reference ranges stay in USD (rule 12)");
    }

    [Fact]
    public async Task Seed_ShouldGiveJoinNicaraguaItsSampleDataInNicaragua()
    {
        _ = _factory.CreateClient();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (joinId, niId) = await CompanyIdsAsync(db);

        (await db.Persons.IgnoreQueryFilters().CountAsync(p => p.CompanyId == niId && p.GcRecord == 0))
            .Should().Be(await db.Persons.IgnoreQueryFilters().CountAsync(p => p.CompanyId == joinId && p.GcRecord == 0));
        (await db.Customers.IgnoreQueryFilters().CountAsync(c => c.CompanyId == niId)).Should().BePositive();
        (await db.SalesTeams.IgnoreQueryFilters().CountAsync(t => t.CompanyId == niId && t.GcRecord == 0)).Should().Be(3);
        (await db.Tickets.IgnoreQueryFilters().CountAsync(t => t.CompanyId == niId)).Should().Be(7);
        (await db.CalendarCompanies.IgnoreQueryFilters().AnyAsync(c => c.CompanyId == niId && c.GcRecord == 0)).Should().BeTrue();
        (await db.EmailTemplates.IgnoreQueryFilters().CountAsync(t => t.CompanyId == niId && t.GcRecord == 0)).Should().Be(4);

        var nicaragua = await db.Countries.IgnoreQueryFilters().SingleAsync(c => c.IsoCode == "NI");
        var addresses = await db.PersonAddresses.IgnoreQueryFilters().Where(a => a.CompanyId == niId).ToListAsync();
        addresses.Should().NotBeEmpty();
        addresses.Should().OnlyContain(a => a.CountryId == nicaragua.Id);
        var municipalityNames = await db.Municipalities.IgnoreQueryFilters()
            .Where(m => addresses.Select(a => a.MunicipalityId).Contains(m.Id))
            .Select(m => m.Name)
            .ToListAsync();
        municipalityNames.Should().HaveCount(20, "the 30 persons with an address are spread over the 20 municipalities");
        municipalityNames.Should().BeSubsetOf(DevelopmentCompanyProfiles.Nicaragua.MunicipalityNames);
    }

    [Fact]
    public async Task Seed_RunTwice_ShouldNotDuplicateAndShouldLeaveJoin001Unchanged()
    {
        _ = _factory.CreateClient();

        Snapshot before;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            before = await SnapshotAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
            await seeder.SeedDevelopmentCompaniesAsync();
            await seeder.SeedDevelopmentCompaniesAsync();
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var after = await SnapshotAsync(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
            after.Should().BeEquivalentTo(before);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedMenuAndPermissionsAsync(forceReseed: true);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var (joinId, niId) = await CompanyIdsAsync(db);
            var states = await db.SeedStates.IgnoreQueryFilters().Where(s => s.CompanyId == joinId || s.CompanyId == niId).ToListAsync();
            states.Select(s => s.CompanyId).Should().BeEquivalentTo([joinId, niId], "one SeedState row per development company");
            states.Select(s => s.Checksum).Distinct().Should().ContainSingle();
        }
    }

    private sealed record Snapshot(
        Dictionary<string, decimal> JoinPrices,
        List<(Guid UserId, bool IsDefault)> JoinMemberships,
        int JoinPermissions,
        Dictionary<string, int> NicaraguaCounts);

    private static async Task<Snapshot> SnapshotAsync(ApplicationDbContext db)
    {
        var (joinId, niId) = await CompanyIdsAsync(db);
        var joinMemberships = (await db.UserCompanies.IgnoreQueryFilters().Where(uc => uc.CompanyId == joinId)
                .OrderBy(uc => uc.UserId).Select(uc => new { uc.UserId, uc.IsDefault }).ToListAsync())
            .Select(x => (x.UserId, x.IsDefault)).ToList();

        var counts = new Dictionary<string, int>
        {
            ["Companies"] = await db.Companies.IgnoreQueryFilters().CountAsync(c => c.TaxId == "JOIN-NI-001"),
            ["UserCompanies"] = await db.UserCompanies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["UserRoleCompanies"] = await db.UserRoleCompanies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["RoleSystemOptions"] = await db.RoleSystemOptions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["CompanyModules"] = await db.CompanyModules.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["CompanyCurrencies"] = await db.CompanyCurrencies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["ExchangeRates"] = await db.ExchangeRates.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["Persons"] = await db.Persons.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["PersonAddresses"] = await db.PersonAddresses.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["Customers"] = await db.Customers.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["Products"] = await db.Products.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["Leads"] = await db.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["Tickets"] = await db.Tickets.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["SalesTeams"] = await db.SalesTeams.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["TicketUserCompanies"] = await db.TicketUserCompanies.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["CalendarUserConfigurations"] = await db.CalendarUserConfigurations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["IncomeRanges"] = await db.IncomeRanges.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId),
            ["EmailTemplates"] = await db.EmailTemplates.IgnoreQueryFilters().CountAsync(x => x.CompanyId == niId)
        };

        return new Snapshot(
            await PricesAsync(db, joinId),
            joinMemberships,
            await db.RoleSystemOptions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == joinId),
            counts);
    }

    private static async Task<(Guid JoinId, Guid NicaraguaId)> CompanyIdsAsync(ApplicationDbContext db)
    {
        var ids = await db.Companies.IgnoreQueryFilters()
            .Where(c => c.TaxId == "JOIN-001" || c.TaxId == "JOIN-NI-001")
            .ToDictionaryAsync(c => c.TaxId, c => c.Id);
        ids.Should().ContainKeys("JOIN-001", "JOIN-NI-001");
        return (ids["JOIN-001"], ids["JOIN-NI-001"]);
    }

    private static Task<string> BaseCurrencyCodeAsync(ApplicationDbContext db, Guid companyId) =>
        (from cc in db.CompanyCurrencies.IgnoreQueryFilters()
         join c in db.Currencies.IgnoreQueryFilters() on cc.CurrencyId equals c.Id
         where cc.CompanyId == companyId && cc.IsBase && cc.GcRecord == 0
         select c.Code).SingleAsync();

    private static Task<Dictionary<string, decimal>> PricesAsync(ApplicationDbContext db, Guid companyId) =>
        db.Products.IgnoreQueryFilters()
            .Where(p => p.CompanyId == companyId && p.GcRecord == 0)
            .ToDictionaryAsync(p => p.Code, p => p.ReferencePrice);

    private static async Task<List<(Guid UserId, Guid RoleId)>> RolePairsAsync(ApplicationDbContext db, Guid companyId) =>
        (await db.UserRoleCompanies.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .Select(x => new { x.UserId, x.RoleId })
            .ToListAsync())
        .Select(x => (x.UserId, x.RoleId)).Distinct().ToList();

    private static async Task<List<(Guid RoleId, Guid SystemOptionId)>> PermissionPairsAsync(ApplicationDbContext db, Guid companyId) =>
        (await db.RoleSystemOptions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.GcRecord == 0)
            .Select(x => new { x.RoleId, x.SystemOptionId })
            .ToListAsync())
        .Select(x => (x.RoleId, x.SystemOptionId)).ToList();
}
