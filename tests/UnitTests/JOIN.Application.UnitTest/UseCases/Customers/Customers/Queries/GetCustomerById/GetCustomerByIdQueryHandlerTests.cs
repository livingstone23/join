using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Customers.Customers.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.Customers.Queries.GetCustomerById;

/// <summary>
/// Contains the unit tests for the customer detail query handler.
/// </summary>
public sealed class GetCustomerByIdQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    internal static readonly Guid StageId = Guid.NewGuid();

    internal static readonly string[] Columns =
    [
        "Id", "CompanyId", "PersonId", "UserId", "CustomerCode", "CustomerStageId", "CustomerStageCode", "CustomerStageName",
        "PersonName", "UserEmail", "IsActive", "ActivatedAt", "DeactivatedAt", "CreatedAt",
        "LastStatusAction", "LastStatusSource", "LastStatusAt"
    ];

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetCustomerByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCustomerIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateHandler().Handle(new GetCustomerByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenCustomerExists_ShouldReturnDetail()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id, CompanyId)));

        var response = await context.CreateHandler().Handle(new GetCustomerByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CustomerCode.Should().Be("C000000001");
        response.Data.PersonName.Should().Be("Ana Lopez");
        response.Data.CustomerStageCode.Should().Be("CUSTOMER");
        response.Data.LastStatusAction.Should().Be(1);
        context.Connection.LastCommandText.Should().Contain("FROM customer.customers cust").And.NotContain("Admin.Customers");
        context.Connection.LastCommandText.Should().Contain("cust.CompanyId = @TenantId").And.Contain("cust.GcRecord = 0");
        context.Connection.CapturedParameters["TenantId"].Should().Be(CompanyId);
    }

    internal static Dictionary<string, object?> Row(Guid id, Guid companyId) => new()
    {
        ["Id"] = id,
        ["CompanyId"] = companyId,
        ["PersonId"] = Guid.NewGuid(),
        ["UserId"] = Guid.NewGuid(),
        ["CustomerCode"] = "C000000001",
        ["CustomerStageId"] = StageId,
        ["CustomerStageCode"] = "CUSTOMER",
        ["CustomerStageName"] = "Cliente",
        ["PersonName"] = "Ana Lopez",
        ["UserEmail"] = "ana@join.test",
        ["IsActive"] = true,
        ["ActivatedAt"] = DateTime.UtcNow,
        ["DeactivatedAt"] = null,
        ["CreatedAt"] = DateTime.UtcNow,
        ["LastStatusAction"] = 1,
        ["LastStatusSource"] = 2,
        ["LastStatusAt"] = DateTime.UtcNow
    };

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

        public GetCustomerByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
