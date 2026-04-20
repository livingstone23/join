using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Areas.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Areas.Queries.GetAreaById;

/// <summary>
/// Contains the unit tests for the area detail query handler.
/// These tests verify tenant protection, not-found behavior, and the successful detail path.
/// </summary>
public sealed class GetAreaByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetAreaByIdQueryHandlerTestContext();
        var query = new GetAreaByIdQuery(_fixture.Create<Guid>(), Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the area exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaExists_ShouldReturnAreaDetails()
    {
        // Arrange
        var areaId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var entityStatusId = _fixture.Create<Guid>();
        var createdAt = new DateTime(2026, 4, 18, 9, 15, 0, DateTimeKind.Utc);

        var context = new GetAreaByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = areaId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["Name"] = "Operations",
                    ["EntityStatusId"] = entityStatusId,
                    ["EntityStatusName"] = "Active",
                    ["Created"] = createdAt
                }));

        var query = new GetAreaByIdQuery(areaId, companyId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Area retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(areaId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Name.Should().Be("Operations");
        response.Data.EntityStatusId.Should().Be(entityStatusId);
        response.Data.EntityStatusName.Should().Be("Active");
        response.Data.Created.Should().Be(createdAt);

        context.Connection.LastCommandText.Should().Contain("WHERE a.Id = @AreaId");
        context.Connection.LastCommandText.Should().Contain("AND a.CompanyId = @CompanyId");
        context.Connection.CapturedParameters["AreaId"].Should().Be(areaId);
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found branch when the current tenant has no matching area.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaDoesNotExist_ShouldReturnAreaNotFoundError()
    {
        // Arrange
        var context = new GetAreaByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "Name",
                "EntityStatusId",
                "EntityStatusName",
                "Created"));

        var query = new GetAreaByIdQuery(_fixture.Create<Guid>(), _fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("AREA_NOT_FOUND");
        response.Errors.Should().Contain("Area not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the area detail tests.
    /// </summary>
    private sealed class GetAreaByIdQueryHandlerTestContext
    {
        public GetAreaByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetAreaByIdQueryHandler CreateHandler()
        {
            return new GetAreaByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
