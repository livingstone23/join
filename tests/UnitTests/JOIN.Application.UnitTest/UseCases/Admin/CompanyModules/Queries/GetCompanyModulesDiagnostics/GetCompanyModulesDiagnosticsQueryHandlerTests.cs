using FluentAssertions;
using JOIN.Application.Common.Settings;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.CompanyModules.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Queries.GetCompanyModulesDiagnostics;

/// <summary>
/// SPEC 48 F / H — diagnostics for the <c>Modules:EnforceCompanyModules</c> switch: base modules, companies
/// with missing base modules, the affected-user count and the switch value. The SQL itself (which companies
/// are listed) is covered against PostgreSQL by <c>ModuleEnforcementPostgreSqlTests</c>.
/// </summary>
public sealed class GetCompanyModulesDiagnosticsQueryHandlerTests
{
    private static readonly string[] CompanyColumns =
        ["CompanyId", "CompanyName", "MissingBaseModules", "AffectedUserCount"];

    [Fact]
    public async Task Handle_ShouldMapBaseModulesAndCompaniesWithProblems()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext(enforce: false);
        context.Connection.SetResults(
            new FakeResultSet(
                [
                    new Dictionary<string, object?> { ["name"] = "Administration" },
                    new Dictionary<string, object?> { ["name"] = "Tickets" }
                ],
                ["name"]),
            new FakeResultSet(
                [
                    new Dictionary<string, object?>
                    {
                        ["CompanyId"] = companyId,
                        ["CompanyName"] = "Empresa X",
                        ["MissingBaseModules"] = new[] { "Tickets" },
                        ["AffectedUserCount"] = 3L
                    }
                ],
                CompanyColumns));

        var response = await context.CreateHandler().Handle(new GetCompanyModulesDiagnosticsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.EnforceCompanyModules.Should().BeFalse();
        response.Data.BaseModules.Should().Equal("Administration", "Tickets");
        var entry = response.Data.Companies.Should().ContainSingle().Subject;
        entry.CompanyId.Should().Be(companyId);
        entry.CompanyName.Should().Be("Empresa X");
        entry.MissingBaseModules.Should().Equal("Tickets");
        entry.UsersWithPermissionsInDisabledModules.Should().Be(3);
    }

    [Fact]
    public async Task Handle_WhenNoCompanyHasProblems_ShouldReturnEmptyListAndSwitchValue()
    {
        var context = new TestContext(enforce: true);
        context.Connection.SetResults(
            new FakeResultSet([new Dictionary<string, object?> { ["name"] = "Tickets" }], ["name"]),
            FakeResultSet.Empty(CompanyColumns));

        var response = await context.CreateHandler().Handle(new GetCompanyModulesDiagnosticsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.EnforceCompanyModules.Should().BeTrue();
        response.Data.Companies.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenMissingBaseModulesIsNull_ShouldReturnEmptyArray()
    {
        var context = new TestContext(enforce: false);
        context.Connection.SetResults(
            FakeResultSet.Empty("name"),
            new FakeResultSet(
                [
                    new Dictionary<string, object?>
                    {
                        ["CompanyId"] = Guid.NewGuid(),
                        ["CompanyName"] = "Empresa Y",
                        ["MissingBaseModules"] = null,
                        ["AffectedUserCount"] = 1L
                    }
                ],
                CompanyColumns));

        var response = await context.CreateHandler().Handle(new GetCompanyModulesDiagnosticsQuery(), CancellationToken.None);

        var entry = response.Data!.Companies.Should().ContainSingle().Subject;
        entry.MissingBaseModules.Should().BeEmpty();
        entry.UsersWithPermissionsInDisabledModules.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ShouldQueryActiveCompaniesAndBothDisabledModuleCases()
    {
        var context = new TestContext(enforce: false);
        context.Connection.SetResults(FakeResultSet.Empty("name"), FakeResultSet.Empty(CompanyColumns));

        await context.CreateHandler().Handle(new GetCompanyModulesDiagnosticsQuery(), CancellationToken.None);

        var sql = context.Connection.LastCommandText;
        sql.Should().Contain("common.companies");
        sql.Should().Contain("isactive = TRUE AND gcrecord = 0");
        sql.Should().Contain("sm.isactive = FALSE OR cm.id IS NULL");
    }

    private sealed class TestContext(bool enforce)
    {
        public FakeDbConnection Connection { get; } = new();

        public GetCompanyModulesDiagnosticsQueryHandler CreateHandler()
        {
            var factory = new Mock<ISqlConnectionFactory>();
            factory.Setup(x => x.CreateConnection()).Returns(Connection);
            return new GetCompanyModulesDiagnosticsQueryHandler(
                factory.Object,
                Options.Create(new ModuleEnforcementSettings { EnforceCompanyModules = enforce }));
        }
    }
}
