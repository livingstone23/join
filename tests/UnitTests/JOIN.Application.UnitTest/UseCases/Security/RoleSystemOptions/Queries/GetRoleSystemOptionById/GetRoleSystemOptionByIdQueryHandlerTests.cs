using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.RoleSystemOptions.Queries.GetRoleSystemOptionById;

/// <summary>
/// Contains unit tests for the role system option detail query handler.
/// </summary>
public sealed class GetRoleSystemOptionByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the error branch when the authenticated company context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetRoleSystemOptionByIdQueryHandlerTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    /// <summary>
    /// Verifies the not-found branch when no active rule matches the tenant and identifier.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleSystemOptionDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetRoleSystemOptionByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "RoleId",
                "RoleName",
                "SystemOptionId",
                "SystemOptionName",
                "CanRead",
                "CanCreate",
                "CanUpdate",
                "CanDelete",
                "Created"));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_SYSTEM_OPTION_NOT_FOUND");
        response.Errors.Should().Contain("Role system option not found.");
    }

    /// <summary>
    /// Verifies the happy path when the rule exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleSystemOptionExists_ShouldReturnRoleSystemOptionDetails()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var roleSystemOptionId = _fixture.Create<Guid>();
        var context = new GetRoleSystemOptionByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = roleSystemOptionId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["RoleId"] = _fixture.Create<Guid>(),
                    ["RoleName"] = "Admin",
                    ["SystemOptionId"] = _fixture.Create<Guid>(),
                    ["SystemOptionName"] = "Users",
                    ["CanRead"] = true,
                    ["CanCreate"] = true,
                    ["CanUpdate"] = true,
                    ["CanDelete"] = false,
                    ["Created"] = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetRoleSystemOptionByIdQuery(roleSystemOptionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Role system option retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(roleSystemOptionId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.RoleName.Should().Be("Admin");
        response.Data.SystemOptionName.Should().Be("Users");

        context.Connection.LastCommandText.Should().Contain("WHERE rso.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND rso.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().Contain("AND rso.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(roleSystemOptionId);
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    private sealed class GetRoleSystemOptionByIdQueryHandlerTestContext
    {
        public GetRoleSystemOptionByIdQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetRoleSystemOptionByIdQueryHandler CreateHandler()
        {
            return new GetRoleSystemOptionByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
