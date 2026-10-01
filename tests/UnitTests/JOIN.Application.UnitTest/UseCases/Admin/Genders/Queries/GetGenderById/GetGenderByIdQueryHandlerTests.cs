using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Genders.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Queries.GetGenderById;

/// <summary>
/// Contains the unit tests for the gender detail query handler.
/// </summary>
public sealed class GetGenderByIdQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetGenderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenGenderIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "Code", "Name", "IsActive", "CreatedAt"));

        var response = await context.CreateHandler().Handle(new GetGenderByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("GENDER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenGenderExists_ShouldReturnTenantScopedDetail()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = id,
            ["CompanyId"] = CompanyId,
            ["CompanyName"] = "JOIN CRM",
            ["Code"] = "M",
            ["Name"] = "Masculino",
            ["IsActive"] = true,
            ["CreatedAt"] = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        }));

        var response = await context.CreateHandler().Handle(new GetGenderByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be(id);
        response.Data.Code.Should().Be("M");
        context.Connection.LastCommandText.Should().Contain("g.CompanyId = @CompanyId").And.Contain("g.GcRecord = 0");
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

        public GetGenderByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
