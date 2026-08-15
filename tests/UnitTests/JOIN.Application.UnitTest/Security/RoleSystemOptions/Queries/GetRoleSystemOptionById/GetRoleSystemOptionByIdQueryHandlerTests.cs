using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Queries.GetRoleSystemOptionById;

/// <summary>
/// Unit tests for GetRoleSystemOptionByIdQueryHandler.
/// Verifies the SPEC 22 SQL projection: the 5 new columns (CanDownload, CanExport, CanExecute,
/// IsVisibleMenu, OrderMenu) appear in the SELECT produced by BuildByIdSql.
/// </summary>
public sealed class GetRoleSystemOptionByIdQueryHandlerTests
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
    /// Happy path: the SELECT emitted by BuildByIdSql contains all 5 new columns.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRuleExists_ShouldReturnDtoAndProjectAllColumns()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(),
            ["CompanyId"] = Guid.NewGuid(),
            ["CompanyName"] = "Acme",
            ["RoleId"] = Guid.NewGuid(),
            ["RoleName"] = "Admin",
            ["SystemOptionId"] = Guid.NewGuid(),
            ["SystemOptionName"] = "Tickets",
            ["CanRead"] = true,
            ["CanCreate"] = true,
            ["CanUpdate"] = true,
            ["CanDelete"] = true,
            ["CanDownload"] = true,
            ["CanExport"] = false,
            ["CanExecute"] = true,
            ["IsVisibleMenu"] = true,
            ["OrderMenu"] = 0,
            ["Created"] = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
        }));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();

        context.Connection.LastCommandText.Should().Contain("rso.CanDownload");
        context.Connection.LastCommandText.Should().Contain("rso.CanExport");
        context.Connection.LastCommandText.Should().Contain("rso.CanExecute");
        context.Connection.LastCommandText.Should().Contain("rso.IsVisibleMenu");
        context.Connection.LastCommandText.Should().Contain("rso.OrderMenu");
    }

    /// <summary>
    /// CompanyId == Guid.Empty short-circuits with INVALID_COMPANY_ID before opening a connection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.Setup(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Empty result returns ROLE_SYSTEM_OPTION_NOT_FOUND.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRuleMissing_ShouldReturnNotFound()
    {
        var context = new TestContext();
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_SYSTEM_OPTION_NOT_FOUND");
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

        public GetRoleSystemOptionByIdQueryHandler CreateHandler()
        {
            return new GetRoleSystemOptionByIdQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
        }
    }
}