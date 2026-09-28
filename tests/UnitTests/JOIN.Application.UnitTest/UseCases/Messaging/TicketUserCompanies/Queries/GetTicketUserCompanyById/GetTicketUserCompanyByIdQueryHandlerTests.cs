using AutoFixture;
using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;

/// <summary>
/// Unit tests for <see cref="GetTicketUserCompanyByIdQueryHandler"/>.
/// </summary>
public sealed class GetTicketUserCompanyByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetTicketUserCompanyByIdQueryHandlerTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new GetTicketUserCompanyByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRowExists_ShouldReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var rowId = _fixture.Create<Guid>();
        var userId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompanyByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = rowId,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["UserId"] = userId,
                ["UserName"] = "Manager Test",
                ["UserEmail"] = "manager@join.com",
                ["IsSuperAdminTicket"] = true,
                ["CanFinishTicket"] = true,
                ["CanResolveTicket"] = true,
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var handlerInstance = context.CreateHandler();
        var result = await handlerInstance.Handle(new GetTicketUserCompanyByIdQuery(rowId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Id.Should().Be(rowId);
        result.Data.CompanyName.Should().Be("JOIN");
        result.Data.UserName.Should().Be("Manager Test");
        result.Data.IsSuperAdminTicket.Should().BeTrue();
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_WhenRowDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompanyByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail",
            "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"));

        var handlerInstance = context.CreateHandler();
        var result = await handlerInstance.Handle(new GetTicketUserCompanyByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("TICKET_USER_COMPANY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRowBelongsToDifferentTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketUserCompanyByIdQueryHandlerTestContext(companyId);

        // SQL filters by @TenantId; a row owned by another tenant never reaches the result set.
        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail",
            "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"));

        var handlerInstance = context.CreateHandler();
        var result = await handlerInstance.Handle(new GetTicketUserCompanyByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Be("TICKET_USER_COMPANY_NOT_FOUND");
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    private sealed class GetTicketUserCompanyByIdQueryHandlerTestContext
    {
        public GetTicketUserCompanyByIdQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketUserCompanyByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
