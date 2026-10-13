using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Infrastructure.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JOIN.Application.UnitTest.Security;

/// <summary>
/// SPEC 48 B / H — covers <see cref="IPermissionService.GetModuleAccessAsync"/>: name
/// normalization, per-company caching, and per-company / global invalidation paths.
/// </summary>
public sealed class PermissionServiceModuleAccessTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    /// <summary>
    /// Builds a <see cref="PermissionService"/> whose Dapper connection is wired to a
    /// <see cref="FakeSqlConnectionFactory"/>. Tests push rows through
    /// <see cref="FakeSqlConnectionFactory.SetResults"/>.
    /// </summary>
    private static (PermissionService service, FakeSqlConnectionFactory factory, MemoryCache cache) CreateService()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var factory = new FakeSqlConnectionFactory();
        var dbContextOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"module-access-tests-{Guid.NewGuid()}")
            .Options;
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(Guid.NewGuid().ToString());
        currentUser.Setup(c => c.CompanyId).Returns(CompanyId);
        var interceptor = new AuditableEntitySaveChangesInterceptor(currentUser.Object);
        var ctx = new ApplicationDbContext(dbContextOptions, interceptor, currentUser.Object);
        return (new PermissionService(ctx, cache, factory), factory, cache);
    }

    /// <summary>
    /// Builds a single-row result set that mimics the shape of the Dapper query.
    /// </summary>
    private static FakeResultSet BuildRow(string controllerName, string moduleName, bool isEnabled)
    {
        // Keys match the SQL aliases declared by PermissionService.ModuleAccessQuerySql so the
        // fake reader's case-insensitive lookup resolves to the right property on ModuleAccessSqlRow.
        return new FakeResultSet(
            rows: new[]
            {
                new Dictionary<string, object?>
                {
                    ["ControllerName"] = controllerName,
                    ["ModuleName"] = moduleName,
                    ["IsEnabled"] = isEnabled
                }
            },
            columns: new[] { "ControllerName", "ModuleName", "IsEnabled" });
    }

    [Fact]
    public async Task GetModuleAccessAsync_WhenModuleIsEnabledForCompany_ShouldReturnEnabled()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: true));

        var result = await service.GetModuleAccessAsync(CompanyId, "Tickets");

        result.IsEnabled.Should().BeTrue();
        result.ModuleName.Should().Be("Tickets");
    }

    [Fact]
    public async Task GetModuleAccessAsync_WhenModuleIsDisabledForCompany_ShouldReturnDisabled()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: false));

        var result = await service.GetModuleAccessAsync(CompanyId, "Tickets");

        result.IsEnabled.Should().BeFalse();
        result.ModuleName.Should().Be("Tickets");
    }

    [Fact]
    public async Task GetModuleAccessAsync_WhenResourceHasNoSystemOption_ShouldReturnEnabled()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(FakeResultSet.Empty("controllername", "name", "isenabled"));

        var result = await service.GetModuleAccessAsync(CompanyId, "UnknownResource");

        // A resource without a SystemOption is not module-scoped — the gate passes and the
        // permission check is what decides.
        result.IsEnabled.Should().BeTrue();
        result.ModuleName.Should().BeNull();
    }

    [Fact]
    public async Task GetModuleAccessAsync_ShouldNormalizeControllerName_SingularAndPlural()
    {
        var (service, factory, _) = CreateService();
        // Seed registers "Countries" (plural). Lookup uses "Country" (singular).
        factory.SetResults(BuildRow("Countries", "Customers", isEnabled: true));

        var singularResult = await service.GetModuleAccessAsync(CompanyId, "Country");
        var pluralResult = await service.GetModuleAccessAsync(CompanyId, "Countries");

        singularResult.IsEnabled.Should().BeTrue();
        pluralResult.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetModuleAccessAsync_ShouldStripControllerSuffix()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Persons", "Customers", isEnabled: true));

        var result = await service.GetModuleAccessAsync(CompanyId, "PersonsController");

        result.IsEnabled.Should().BeTrue();
        result.ModuleName.Should().Be("Customers");
    }

    [Fact]
    public async Task GetModuleAccessAsync_ShouldCacheResult_AndReturnCachedValueWithoutHittingDbAgain()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: true));

        await service.GetModuleAccessAsync(CompanyId, "Tickets");
        var lastCommandAfterFirst = factory.LastCommandText;

        await service.GetModuleAccessAsync(CompanyId, "Tickets");
        var lastCommandAfterSecond = factory.LastCommandText;

        // Cache hit: the second call did not issue a new query.
        lastCommandAfterSecond.Should().Be(lastCommandAfterFirst);
    }

    [Fact]
    public async Task InvalidateCompanyModuleCacheAsync_ShouldForceNextCallToRereadDatabase()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: true));

        await service.GetModuleAccessAsync(CompanyId, "Tickets");
        await service.InvalidateCompanyModuleCacheAsync(CompanyId);

        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: false));
        var result = await service.GetModuleAccessAsync(CompanyId, "Tickets");

        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task InvalidateModuleCacheForAllCompaniesAsync_ShouldDropEveryCompanySnapshot()
    {
        var (service, factory, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: true));

        // Prime one company snapshot, then invalidate globally.
        await service.GetModuleAccessAsync(CompanyId, "Tickets");
        await service.InvalidateModuleCacheForAllCompaniesAsync();

        // After global invalidation, the next call must rebuild.
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: false));
        var result = await service.GetModuleAccessAsync(CompanyId, "Tickets");

        result.IsEnabled.Should().BeFalse();
    }

    /// <summary>
    /// PermissionService is scoped: the request that changes a SystemModule is not the one that cached the
    /// snapshot. Global invalidation from one instance must drop entries created by another one.
    /// </summary>
    [Fact]
    public async Task InvalidateModuleCacheForAllCompaniesAsync_FromAnotherInstance_ShouldDropSharedCacheEntries()
    {
        var (reader, factory, cache) = CreateService();
        var (writer, _, _) = CreateService();
        factory.SetResults(BuildRow("Tickets", "Tickets", isEnabled: true));
        await reader.GetModuleAccessAsync(CompanyId, "Tickets");

        await writer.InvalidateModuleCacheForAllCompaniesAsync();

        cache.TryGetValue($"company-modules:v1:{CompanyId}", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetModuleAccessAsync_ShouldReturnIsEnabledTrue_WhenResourceIsEnabledByAtLeastOneOfItsModules()
    {
        var (service, factory, _) = CreateService();
        // Two modules own the same controller name; only the second is enabled.
        // Both rows live in the same result set (Dapper QueryAsync yields one grid per call).
        var combined = new FakeResultSet(
            rows: new[]
            {
                new Dictionary<string, object?>
                {
                    ["ControllerName"] = "Tickets",
                    ["ModuleName"] = "Tickets",
                    ["IsEnabled"] = false
                },
                new Dictionary<string, object?>
                {
                    ["ControllerName"] = "Tickets",
                    ["ModuleName"] = "Marketing",
                    ["IsEnabled"] = true
                }
            },
            columns: new[] { "ControllerName", "ModuleName", "IsEnabled" });
        factory.SetResults(combined);

        var result = await service.GetModuleAccessAsync(CompanyId, "Tickets");

        result.IsEnabled.Should().BeTrue();
    }
}