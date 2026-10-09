using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Genders.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Queries.GetGenders;

/// <summary>
/// SPEC 41 visibility rules for <see cref="GetGendersQueryHandler"/>: deleted rows only for a SuperAdmin who asks
/// (<c>includeDeleted</c>), and the requested company only honored for a SuperAdmin (<see cref="TenantResolver"/>).
/// </summary>
public sealed class GetGendersQueryHandlerVisibilityTests
{
    private static readonly Guid TokenCompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ByDefault_ShouldExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetGendersQuery(IncludeDeleted: null, CompanyId: null));

        context.Connection.LastCommandText.Should().Contain(" g.GcRecord = 0");
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsDeleted_ShouldStillExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: false);

        await context.HandleAsync(new GetGendersQuery(IncludeDeleted: true, CompanyId: TokenCompanyId));

        context.Connection.LastCommandText.Should().Contain(" g.GcRecord = 0");
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsDeleted_ShouldIncludeDeletedRowsAndSelectGcRecord()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetGendersQuery(IncludeDeleted: true, CompanyId: TokenCompanyId));

        context.Connection.LastCommandText.Should().NotContain(" g.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("g.GcRecord,");
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsAnotherCompany_ShouldUseTokenCompany()
    {
        var context = new TestContext(isSuperAdmin: false);

        await context.HandleAsync(new GetGendersQuery(IncludeDeleted: null, CompanyId: OtherCompanyId));

        context.Connection.CapturedParameters["CompanyId"].Should().Be(TokenCompanyId);
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsAnotherCompany_ShouldUseRequestedCompany()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetGendersQuery(IncludeDeleted: null, CompanyId: OtherCompanyId));

        context.Connection.CapturedParameters["CompanyId"].Should().Be(OtherCompanyId);
    }

    private sealed class TestContext
    {
        public TestContext(bool isSuperAdmin)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(TokenCompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            Connection.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public async Task HandleAsync(GetGendersQuery query)
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

        private GetGendersQueryHandler CreateHandler() => new GetGendersQueryHandler(ConnectionFactoryMock.Object, Options.Create(new PaginationSettings()), CurrentUserServiceMock.Object);
    }
}
