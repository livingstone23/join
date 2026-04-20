using AutoFixture;
using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TimeUnits.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TimeUnits.Queries.GetTimeUnitById;

/// <summary>
/// Contains the unit tests for the single time unit detail query.
/// These tests validate the tenant guard, the happy path, and the thrown not-found exception.
/// </summary>
public sealed class GetTimeUnitByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetTimeUnitByIdQueryTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTimeUnitByIdQuery(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the time unit exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityExists_ShouldReturnTimeUnitDetails()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var context = new GetTimeUnitByIdQueryTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = entityId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Hours",
                    ["Code"] = 1,
                    ["IsActive"] = true,
                    ["CreatedAt"] = DateTime.UtcNow
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetTimeUnitByIdQuery(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Time unit retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entityId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.Name.Should().Be("Hours");

        context.Connection.LastCommandText.Should().Contain("WHERE tu.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND tu.CompanyId = @TenantId");
        context.Connection.LastCommandText.Should().Contain("AND tu.GcRecord = 0");
    }

    /// <summary>
    /// Verifies that the handler throws when the requested time unit does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldThrowNotFoundException()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var entityId = _fixture.Create<Guid>();
        var context = new GetTimeUnitByIdQueryTestContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "Code",
                "IsActive",
                "CreatedAt"));

        var handler = context.CreateHandler();

        // Act
        var action = async () => await handler.Handle(new GetTimeUnitByIdQuery(entityId), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<NotFoundException>().WithMessage("Time unit not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetTimeUnitByIdQueryTestContext
    {
        public GetTimeUnitByIdQueryTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTimeUnitByIdQueryHandler CreateHandler()
        {
            return new GetTimeUnitByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
