using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.GetTickets;

/// <summary>
/// SPEC 41 (Etapa 4) visibility rules for <see cref="GetTicketsQueryHandler"/>: deleted rows only for a SuperAdmin who asks
/// (<c>includeDeleted</c>), and the requested company only honored for a SuperAdmin (<see cref="TenantResolver"/>).
/// </summary>
public sealed class GetTicketsQueryHandlerVisibilityTests
{
    private static readonly Guid TokenCompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ByDefault_ShouldExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetTicketsQuery(IncludeDeleted: null, CompanyId: null));

        context.Connection.LastCommandText.Should().Contain("t.GcRecord = 0");
        context.Connection.CapturedParameters["TenantId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsDeletedAndAnotherCompany_ShouldIgnoreBoth()
    {
        var context = new TestContext(isSuperAdmin: false);

        await context.HandleAsync(new GetTicketsQuery(IncludeDeleted: true, CompanyId: OtherCompanyId));

        context.Connection.LastCommandText.Should().Contain("t.GcRecord = 0");
        context.Connection.CapturedParameters["TenantId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsDeletedAndAnotherCompany_ShouldHonorBoth()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetTicketsQuery(IncludeDeleted: true, CompanyId: OtherCompanyId));

        context.Connection.LastCommandText.Should().NotContain("t.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("t.GcRecord,");
        context.Connection.CapturedParameters["TenantId"].Should().Be(OtherCompanyId);
    }

    private sealed class TestContext
    {
        public TestContext(bool isSuperAdmin)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(TokenCompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            Connection.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public async Task HandleAsync(GetTicketsQuery query)
        {
            try
            {
                await CreateHandler().Handle(query, CancellationToken.None);
            }
            catch (NotFoundException)
            {
                // Some handlers throw when no row matches; only the captured SQL matters here.
            }
        }

        private GetTicketsQueryHandler CreateHandler() => new GetTicketsQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object, Options.Create(new PaginationSettings()));
    }
}
