using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.SystemOptions.Queries;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Queries.GetSystemOptionById;

/// <summary>
/// Unit tests for GetSystemOptionByIdQueryHandler.
/// Verifies the SPEC 21 JOIN projection: ModuleName comes from Security.SystemModules,
/// ParentName from LEFT JOIN Security.SystemOptions, and the new flags (CanDownload,
/// CanExport, CanExecute, IsVisibleMenu, OrderMenu) are included in the SELECT.
/// </summary>
public sealed class GetSystemOptionByIdQueryHandlerTests
{
    private static readonly string[] Columns =
    [
        "Id", "ModuleId", "ModuleName",
        "Name", "Route", "Icon",
        "ParentId", "ParentName", "ControllerName",
        "CanRead", "CanCreate", "CanUpdate", "CanDelete",
        "CanDownload", "CanExport", "CanExecute",
        "IsVisibleMenu", "OrderMenu", "Created"
    ];

    /// <summary>
    /// Happy path: row exists, JOINs populate ModuleName and ParentName, DTO carries the new flags.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemOptionExists_ShouldProjectJoinsAndReturnDto()
    {
        var id = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var context = new TestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = id,
                ["ModuleId"] = moduleId,
                ["ModuleName"] = "Security",
                ["Name"] = "Users",
                ["Route"] = "/users",
                ["Icon"] = "users",
                ["ParentId"] = parentId,
                ["ParentName"] = "Administration",
                ["ControllerName"] = "Users",
                ["CanRead"] = true,
                ["CanCreate"] = true,
                ["CanUpdate"] = false,
                ["CanDelete"] = false,
                ["CanDownload"] = true,
                ["CanExport"] = true,
                ["CanExecute"] = false,
                ["IsVisibleMenu"] = true,
                ["OrderMenu"] = 3,
                ["Created"] = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
            }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetSystemOptionByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(id);
        response.Data.ModuleId.Should().Be(moduleId);
        response.Data.ModuleName.Should().Be("Security");
        response.Data.ParentName.Should().Be("Administration");
        response.Data.CanDownload.Should().BeTrue();
        response.Data.CanExport.Should().BeTrue();
        response.Data.CanExecute.Should().BeFalse();
        response.Data.IsVisibleMenu.Should().BeTrue();
        response.Data.OrderMenu.Should().Be(3);

        context.Connection.LastCommandText.Should().Contain("INNER JOIN [Admin].[SystemModules] m ON m.Id = o.ModuleId");
        context.Connection.LastCommandText.Should().Contain("LEFT JOIN Security.SystemOptions p ON p.Id = o.ParentId");
        context.Connection.LastCommandText.Should().Contain("o.CanDownload");
        context.Connection.LastCommandText.Should().Contain("o.CanExport");
        context.Connection.LastCommandText.Should().Contain("o.CanExecute");
        context.Connection.LastCommandText.Should().Contain("o.IsVisibleMenu");
        context.Connection.LastCommandText.Should().Contain("o.OrderMenu");
        context.Connection.CapturedParameters["Id"].Should().Be(id);
    }

    /// <summary>
    /// Not-found: empty result set, handler throws NotFoundException.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemOptionDoesNotExist_ShouldThrowNotFound()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var handler = context.CreateHandler();
        var act = async () => await handler.Handle(new GetSystemOptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<JOIN.Application.Exceptions.NotFoundException>();
    }

    private sealed class TestContext
    {
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public TestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public GetSystemOptionByIdQueryHandler CreateHandler() => new(ConnectionFactoryMock.Object);
    }
}
