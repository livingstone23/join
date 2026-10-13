using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetAssignableTicketUsers;
using JOIN.Application.Interface;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetAssignableTicketUsers;

/// <summary>
/// Unit tests for <see cref="GetAssignableTicketUsersQueryHandler"/> (SPEC 53).
/// Verifies the tenant filter, the <c>CanResolveTicket = TRUE</c> filter, and
/// the order of the result — all derived from a Dapper SQL fixture, no EF.
/// </summary>
public sealed class GetAssignableTicketUsersQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new GetAssignableTicketUsersQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRowsExist_ShouldReturnThemAndQueryByTenant()
    {
        var companyId = _fixture.Create<Guid>();
        var u1 = _fixture.Create<Guid>();
        var u2 = _fixture.Create<Guid>();
        var context = new TestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["UserId"] = u1,
                    ["UserName"] = "Ana Pérez",
                    ["UserEmail"] = "ana@join.com"
                },
                new Dictionary<string, object?>
                {
                    ["UserId"] = u2,
                    ["UserName"] = "Luis Soto",
                    ["UserEmail"] = "luis@join.com"
                }));

        var response = await context.CreateHandler().Handle(new GetAssignableTicketUsersQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![0].UserId.Should().Be(u1);
        response.Data[0].UserName.Should().Be("Ana Pérez");
        response.Data[0].UserEmail.Should().Be("ana@join.com");
        response.Data[1].UserId.Should().Be(u2);

        // SQL must filter by tenant and by active + CanResolveTicket rows only.
        context.Connection.LastCommandText.Should().Contain("tuc.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("tuc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("tuc.CanResolveTicket = TRUE");
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminTargetsAnotherCompany_ShouldStillUseTokenCompany()
    {
        var tokenCompany = _fixture.Create<Guid>();
        var otherCompany = _fixture.Create<Guid>();
        var context = new TestContext(tokenCompany, isSuperAdmin: false);
        context.Connection.SetResults(FakeResultSet.Empty("UserId", "UserName", "UserEmail"));

        await context.CreateHandler().Handle(new GetAssignableTicketUsersQuery(CompanyId: otherCompany), CancellationToken.None);

        context.Connection.CapturedParameters["TenantId"].Should().Be(tokenCompany);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminTargetsAnotherCompany_ShouldQueryThatCompany()
    {
        var tokenCompany = _fixture.Create<Guid>();
        var targetCompany = _fixture.Create<Guid>();
        var context = new TestContext(tokenCompany, isSuperAdmin: true);
        context.Connection.SetResults(FakeResultSet.Empty("UserId", "UserName", "UserEmail"));

        await context.CreateHandler().Handle(new GetAssignableTicketUsersQuery(CompanyId: targetCompany), CancellationToken.None);

        context.Connection.CapturedParameters["TenantId"].Should().Be(targetCompany);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId, bool isSuperAdmin = false)
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetAssignableTicketUsersQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}