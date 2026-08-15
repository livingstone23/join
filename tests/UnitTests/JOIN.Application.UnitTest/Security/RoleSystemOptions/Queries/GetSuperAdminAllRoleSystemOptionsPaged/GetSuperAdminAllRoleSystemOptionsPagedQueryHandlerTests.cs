using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Queries.GetSuperAdminAllRoleSystemOptionsPaged;

/// <summary>
/// Unit tests for GetSuperAdminAllRoleSystemOptionsPagedQueryHandler.
/// Verifies SPEC 22 RequireCompanyFilter logic: the company scope is only enforced when
/// the request supplies a CompanyId, and the 5 new filters flow into the WHERE clause.
/// </summary>
public sealed class GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTests
{
    private static readonly string[] Columns =
    [
        "Id", "CompanyId", "CompanyName",
        "RoleId", "RoleName",
        "SystemOptionId", "SystemOptionName",
        "CanRead", "CanCreate", "CanUpdate", "CanDelete",
        "CanDownload", "CanExport", "CanExecute",
        "IsVisibleMenu", "OrderMenu",
        "Created"
    ];

    /// <summary>
    /// When CompanyId is supplied the WHERE clause includes the company filter.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdProvided_ShouldIncludeCompanyFilter()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var query = new GetSuperAdminAllRoleSystemOptionsPagedQuery(
            PageNumber: 1,
            PageSize: 10,
            RoleId: null,
            SystemOptionId: null,
            RoleName: null,
            SystemOptionName: null,
            CompanyName: null,
            CanRead: null,
            CanCreate: null,
            CanUpdate: null,
            CanDelete: null,
            CanDownload: true,
            CanExport: null,
            CanExecute: null,
            IsVisibleMenu: null,
            OrderMenu: null,
            CompanyId: Guid.NewGuid());

        await handler.Handle(query, CancellationToken.None);

        context.Connection.LastCommandText.Should().Contain("AND rso.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().Contain("AND rso.CanDownload = @CanDownload");
        context.Connection.CapturedParameters.Should().ContainKey("CanDownload");
    }

    /// <summary>
    /// When CompanyId is null the WHERE clause does not include the company filter
    /// (cross-tenant listing for SuperAdmin).
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsNull_ShouldOmitCompanyFilter()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var query = new GetSuperAdminAllRoleSystemOptionsPagedQuery(
            PageNumber: 1,
            PageSize: 10,
            RoleId: null,
            SystemOptionId: null,
            RoleName: null,
            SystemOptionName: null,
            CompanyName: null,
            CanRead: null,
            CanCreate: null,
            CanUpdate: null,
            CanDelete: null);

        await handler.Handle(query, CancellationToken.None);

        context.Connection.LastCommandText.Should().NotContain("AND rso.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().NotContain("AND rso.CanDownload = @CanDownload");
    }

    /// <summary>
    /// When no new filters are provided the WHERE clause omits the new predicates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewFiltersAreNull_ShouldOmitThemFromWhereClause()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var query = new GetSuperAdminAllRoleSystemOptionsPagedQuery(
            PageNumber: 1,
            PageSize: 10,
            RoleId: null,
            SystemOptionId: null,
            RoleName: null,
            SystemOptionName: null,
            CompanyName: null,
            CanRead: null,
            CanCreate: null,
            CanUpdate: null,
            CanDelete: null);

        await handler.Handle(query, CancellationToken.None);

        context.Connection.LastCommandText.Should().NotContain("AND rso.CanDownload = @CanDownload");
        context.Connection.LastCommandText.Should().NotContain("AND rso.CanExport = @CanExport");
        context.Connection.LastCommandText.Should().NotContain("AND rso.CanExecute = @CanExecute");
        context.Connection.LastCommandText.Should().NotContain("AND rso.IsVisibleMenu = @IsVisibleMenu");
        context.Connection.LastCommandText.Should().NotContain("AND rso.OrderMenu = @OrderMenu");
    }

    private sealed class TestContext
    {
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public TestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public GetSuperAdminAllRoleSystemOptionsPagedQueryHandler CreateHandler()
        {
            var options = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 20,
                MaxPageSize = 100
            });
            return new GetSuperAdminAllRoleSystemOptionsPagedQueryHandler(ConnectionFactoryMock.Object, options);
        }
    }
}