using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.CompanyModules.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.CompanyModules.Queries.GetCompanyModulesById;

/// <summary>
/// SPEC 43 company scope of <see cref="GetCompanyModulesByIdQueryHandler"/>: a SuperAdmin reads any company's
/// assignment; a SuperAdminCompany only the ones of the company in its token, whatever it requests.
/// </summary>
public sealed class GetCompanyModulesByIdQueryHandlerTests
{
    private static readonly Guid TokenCompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenUserIsNotSuperAdmin_ShouldForceTheTokenCompany()
    {
        var context = new TestContext(isSuperAdmin: false, TokenCompanyId);

        var response = await context.CreateHandler().Handle(new GetCompanyModulesByIdQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_MODULE_NOT_FOUND");
        context.Connection.LastCommandText.Should().Contain("AND cm.CompanyId = @CompanyId");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminHasNoTokenCompany_ShouldReturnInvalidCompany()
    {
        var context = new TestContext(isSuperAdmin: false, Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetCompanyModulesByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsNoCompany_ShouldNotFilterByCompanyAndSelectIsBase()
    {
        var context = new TestContext(isSuperAdmin: true, TokenCompanyId);
        var id = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        context.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["GcRecord"] = 0,
            ["CompanyId"] = companyId,
            ["CompanyName"] = "Other",
            ["ModuleId"] = Guid.NewGuid(),
            ["ModuleName"] = "Tickets",
            ["IsBase"] = true,
            ["IsActive"] = true,
            ["CreatedAt"] = DateTime.UtcNow
        }));

        var response = await context.CreateHandler().Handle(new GetCompanyModulesByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.IsBase.Should().BeTrue();
        context.Connection.LastCommandText.Should().NotContain("AND cm.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().Contain("sm.IsBase");
    }

    private sealed class TestContext
    {
        public TestContext(bool isSuperAdmin, Guid tokenCompanyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tokenCompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            Connection.SetResults(FakeResultSet.Empty("Id"));
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCompanyModulesByIdQueryHandler CreateHandler() => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
