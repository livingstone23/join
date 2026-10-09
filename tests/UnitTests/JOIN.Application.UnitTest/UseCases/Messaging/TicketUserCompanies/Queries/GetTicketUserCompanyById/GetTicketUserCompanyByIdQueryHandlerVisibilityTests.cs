using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanyById;

/// <summary>
/// SPEC 41 (Etapa 4) visibility rules for <see cref="GetTicketUserCompanyByIdQueryHandler"/>: deleted rows only for a SuperAdmin who asks
/// (<c>includeDeleted</c>), and the requested company only honored for a SuperAdmin (<see cref="TenantResolver"/>).
/// </summary>
public sealed class GetTicketUserCompanyByIdQueryHandlerVisibilityTests
{
    private static readonly Guid TokenCompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ByDefault_ShouldExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetTicketUserCompanyByIdQuery(Guid.NewGuid(), null, null));

        context.Connection.LastCommandText.Should().Contain("tuc.GcRecord = 0");
        context.Connection.CapturedParameters["TenantId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsDeletedAndAnotherCompany_ShouldIgnoreBoth()
    {
        var context = new TestContext(isSuperAdmin: false);

        await context.HandleAsync(new GetTicketUserCompanyByIdQuery(Guid.NewGuid(), true, OtherCompanyId));

        context.Connection.LastCommandText.Should().Contain("tuc.GcRecord = 0");
        context.Connection.CapturedParameters["TenantId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsDeletedAndAnotherCompany_ShouldHonorBoth()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetTicketUserCompanyByIdQuery(Guid.NewGuid(), true, OtherCompanyId));

        context.Connection.LastCommandText.Should().NotContain("tuc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("tuc.GcRecord,");
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
            Connection.SetResults(FakeResultSet.Empty("Id"));
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public async Task HandleAsync(GetTicketUserCompanyByIdQuery query)
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

        private GetTicketUserCompanyByIdQueryHandler CreateHandler() => new GetTicketUserCompanyByIdQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
