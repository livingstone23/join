using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Queries.GetRoleSystemOptionsPaged;

/// <summary>
/// Unit tests for GetRoleSystemOptionsPagedQueryHandler.
/// Verifies the SPEC 22 filter additions: when the request supplies a non-null filter for
/// CanDownload / CanExport / CanExecute / IsVisibleMenu / OrderMenu the WHERE clause grows
/// with the corresponding `AND rso.X = @X` predicate; null filters leave the SQL unchanged.
/// </summary>
public sealed class GetRoleSystemOptionsPagedQueryHandlerTests
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
    /// When the new filters are supplied, the generated WHERE clause contains the corresponding predicates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewFiltersProvided_ShouldIncludeThemInWhereClause()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var query = new GetRoleSystemOptionsPagedQuery(
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
            CanExport: true,
            CanExecute: null,
            IsVisibleMenu: true,
            OrderMenu: 0);

        await handler.Handle(query, CancellationToken.None);

        context.Connection.LastCommandText.Should().Contain("AND rso.CanDownload = @CanDownload");
        context.Connection.LastCommandText.Should().Contain("AND rso.CanExport = @CanExport");
        context.Connection.LastCommandText.Should().Contain("AND rso.IsVisibleMenu = @IsVisibleMenu");
        context.Connection.LastCommandText.Should().Contain("AND rso.OrderMenu = @OrderMenu");

        context.Connection.CapturedParameters.Should().ContainKey("CanDownload");
        context.Connection.CapturedParameters["CanDownload"].Should().Be(true);
        context.Connection.CapturedParameters.Should().ContainKey("CanExport");
        context.Connection.CapturedParameters["CanExport"].Should().Be(true);
        context.Connection.CapturedParameters.Should().ContainKey("IsVisibleMenu");
        context.Connection.CapturedParameters["IsVisibleMenu"].Should().Be(true);
        context.Connection.CapturedParameters.Should().ContainKey("OrderMenu");
        context.Connection.CapturedParameters["OrderMenu"].Should().Be(0);
    }

    /// <summary>
    /// When the new filters are null the WHERE clause omits the corresponding predicates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNewFiltersAreNull_ShouldOmitThemFromWhereClause()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var query = new GetRoleSystemOptionsPagedQuery(
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

    /// <summary>
    /// CompanyId == Guid.Empty short-circuits with INVALID_COMPANY_ID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.Setup(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleSystemOptionsPagedQuery(
            PageNumber: null,
            PageSize: null,
            RoleId: null,
            SystemOptionId: null,
            RoleName: null,
            SystemOptionName: null,
            CompanyName: null,
            CanRead: null,
            CanCreate: null,
            CanUpdate: null,
            CanDelete: null), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    private sealed class TestContext
    {
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public TestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            CurrentUserServiceMock.Setup(x => x.CompanyId).Returns(Guid.NewGuid());
        }

        public GetRoleSystemOptionsPagedQueryHandler CreateHandler()
        {
            var options = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 20,
                MaxPageSize = 100
            });
            return new GetRoleSystemOptionsPagedQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object, options);
        }
    }
}
