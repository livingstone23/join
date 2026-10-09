using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Municipalities.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Municipalities.Queries.GetMunicipalities;

/// <summary>
/// SPEC 41 visibility rules for <see cref="GetMunicipalitiesQueryHandler"/>: deleted rows only for a SuperAdmin who asks
/// (<c>includeDeleted</c>), and the requested company only honored for a SuperAdmin (<see cref="TenantResolver"/>).
/// </summary>
public sealed class GetMunicipalitiesQueryHandlerVisibilityTests
{
    private static readonly Guid TokenCompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_ByDefault_ShouldExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetMunicipalitiesQuery(IncludeDeleted: null));

        context.Connection.LastCommandText.Should().Contain(" m.GcRecord = 0");
    }

    [Fact]
    public async Task Handle_WhenNonSuperAdminRequestsDeleted_ShouldStillExcludeDeletedRows()
    {
        var context = new TestContext(isSuperAdmin: false);

        await context.HandleAsync(new GetMunicipalitiesQuery(IncludeDeleted: true));

        context.Connection.LastCommandText.Should().Contain(" m.GcRecord = 0");
    }

    [Fact]
    public async Task Handle_WhenSuperAdminRequestsDeleted_ShouldIncludeDeletedRowsAndSelectGcRecord()
    {
        var context = new TestContext(isSuperAdmin: true);

        await context.HandleAsync(new GetMunicipalitiesQuery(IncludeDeleted: true));

        context.Connection.LastCommandText.Should().NotContain(" m.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("m.GcRecord,");
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

        public async Task HandleAsync(GetMunicipalitiesQuery query)
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

        private GetMunicipalitiesQueryHandler CreateHandler() => new GetMunicipalitiesQueryHandler(ConnectionFactoryMock.Object, Options.Create(new PaginationSettings()), CurrentUserServiceMock.Object);
    }
}
