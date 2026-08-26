using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.SystemOptions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Queries.GetSystemOptionsPaged;

/// <summary>
/// Unit tests for GetSystemOptionsPagedQueryHandler.
/// Verifies the SPEC 21 SQL projection: ModuleName comes from INNER JOIN Security.SystemModules
/// and the new fields (CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu, Icon,
/// ControllerName) appear in the SELECT.
/// </summary>
public sealed class GetSystemOptionsPagedQueryHandlerTests
{
    private static readonly string[] Columns =
    [
        "Id", "ModuleId", "ModuleName",
        "Name", "Route", "Icon", "ControllerName", "ParentId",
        "CanRead", "CanCreate", "CanUpdate", "CanDelete",
        "CanDownload", "CanExport", "CanExecute",
        "IsVisibleMenu", "OrderMenu", "Created"
    ];

    /// <summary>
    /// Happy path: result set returns the projected DTO with ModuleName populated and the new fields.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOptionsExist_ShouldReturnPagedResultsWithJoins()
    {
        var context = new TestContext();
        var moduleId = Guid.NewGuid();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = Guid.NewGuid(),
                    ["ModuleId"] = moduleId,
                    ["ModuleName"] = "Security",
                    ["Name"] = "Users",
                    ["Route"] = "/users",
                    ["Icon"] = "users",
                    ["ControllerName"] = "Users",
                    ["ParentId"] = null,
                    ["CanRead"] = true,
                    ["CanCreate"] = true,
                    ["CanUpdate"] = true,
                    ["CanDelete"] = true,
                    ["CanDownload"] = true,
                    ["CanExport"] = false,
                    ["CanExecute"] = true,
                    ["IsVisibleMenu"] = true,
                    ["OrderMenu"] = 0,
                    ["Created"] = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
                }),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 1 }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetSystemOptionsPagedQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(1);
        var item = response.Data.Items.First();
        item.ModuleName.Should().Be("Security");
        item.CanDownload.Should().BeTrue();
        item.CanExport.Should().BeFalse();
        item.CanExecute.Should().BeTrue();
        item.IsVisibleMenu.Should().BeTrue();
        item.OrderMenu.Should().Be(0);

        context.Connection.LastCommandText.Should().Contain("INNER JOIN [Admin].[SystemModules] m ON m.Id = o.ModuleId");
        context.Connection.LastCommandText.Should().Contain("o.CanDownload");
        context.Connection.LastCommandText.Should().Contain("o.CanExport");
        context.Connection.LastCommandText.Should().Contain("o.CanExecute");
        context.Connection.LastCommandText.Should().Contain("o.IsVisibleMenu");
        context.Connection.LastCommandText.Should().Contain("o.OrderMenu");
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    /// <summary>
    /// SQL Server branch is exercised by default (FakeDbConnection is not Npgsql). Verifies the
    /// OFFSET/FETCH NEXT clause is selected over LIMIT/OFFSET.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalledOnSqlServer_ShouldUseOffsetFetchNextPagination()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        await handler.Handle(new GetSystemOptionsPagedQuery(), CancellationToken.None);

        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        context.Connection.LastCommandText.Should().NotContain("LIMIT @PageSize");
    }

    /// <summary>
    /// Postgres branch: FakeNpgsqlDbConnection triggers the LIMIT/OFFSET clause.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCalledOnPostgres_ShouldUseLimitOffsetPagination()
    {
        var context = new TestContext(useNpgsql: true);
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        await handler.Handle(new GetSystemOptionsPagedQuery(), CancellationToken.None);

        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");
    }

    /// <summary>
    /// Empty result returns a PagedResult with TotalCount=0 and IsSuccess=true.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoOptionsExist_ShouldReturnEmptyPagedResult()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Total"] = 0 }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetSystemOptionsPagedQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
    }

    private sealed class TestContext
    {
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public TestContext(bool useNpgsql = false)
        {
            Connection = useNpgsql ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public GetSystemOptionsPagedQueryHandler CreateHandler()
        {
            var options = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 20,
                MaxPageSize = 100
            });
            return new GetSystemOptionsPagedQueryHandler(ConnectionFactoryMock.Object, options);
        }
    }
}
