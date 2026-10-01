using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Industries.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Industries.Queries.GetIndustryById;

/// <summary>
/// Contains the unit tests for the industry detail query handler.
/// </summary>
public sealed class GetIndustryByIdQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetIndustryByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenIndustryIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "Code", "Name", "Description", "IsActive", "CreatedAt"));

        var response = await context.CreateHandler().Handle(new GetIndustryByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenIndustryExists_ShouldReturnTenantScopedDetail()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["CompanyId"] = CompanyId,
            ["CompanyName"] = "JOIN CRM",
            ["Code"] = "TECH",
            ["Name"] = "Technology",
            ["Description"] = "Software",
            ["IsActive"] = true,
            ["CreatedAt"] = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        }));

        var response = await context.CreateHandler().Handle(new GetIndustryByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be(id);
        response.Data.Code.Should().Be("TECH");
        context.Connection.LastCommandText.Should().Contain("i.CompanyId = @CompanyId").And.Contain("i.GcRecord = 0");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(CompanyId);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetIndustryByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
