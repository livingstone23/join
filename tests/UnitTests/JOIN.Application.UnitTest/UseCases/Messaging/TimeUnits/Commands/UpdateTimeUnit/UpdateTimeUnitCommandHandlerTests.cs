using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TimeUnits.Commands;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TimeUnits.Commands.UpdateTimeUnit;

/// <summary>
/// Contains the unit tests for the time unit update command.
/// These tests verify not-found handling, duplicate validation, and successful persistence.
/// </summary>
public sealed class UpdateTimeUnitCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested entity does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTimeUnitCommandTestContext();
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync((TimeUnit?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the duplicate-name branch while excluding the entity being updated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherEntityUsesTheSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            entity,
            CreateExistingEntity(_fixture.Create<Guid>(), name: "Hours", code: 1)
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, name: "  hours  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch while excluding the entity being updated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherEntityUsesTheSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            entity,
            CreateExistingEntity(_fixture.Create<Guid>(), name: "Days", code: 24)
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, code: 24), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the update is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, name: "  Updated  ", code: 12, isActive: false), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        entity.Name.Should().Be("Updated");
        entity.Code.Should().Be(12);
        entity.IsActive.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the successful update flow and normalized values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateEntityAndReturnSuccess()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, name: "  Weeks  ", code: 168, isActive: false), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Time unit updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entityId);
        response.Data.Name.Should().Be("Weeks");
        response.Data.Code.Should().Be(168);
        response.Data.IsActive.Should().BeFalse();
        context.TimeUnitRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command for time unit scenarios.
    /// </summary>
    private static UpdateTimeUnitCommand CreateValidCommand(Guid id, string name = "Hours", int code = 1, bool isActive = true)
    {
        return new UpdateTimeUnitCommand
        {
            Id = id,
            Name = name,
            Code = code,
            IsActive = isActive
        };
    }

    /// <summary>
    /// Creates a reusable time unit entity for update scenarios.
    /// </summary>
    private static TimeUnit CreateExistingEntity(Guid id, string name = "Minutes", int code = 60)
    {
        var entity = new TimeUnit
        {
            CompanyId = Guid.NewGuid(),
            Name = name,
            Code = code,
            IsActive = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(entity, id);

        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateTimeUnitCommandTestContext
    {
        public UpdateTimeUnitCommandTestContext()
        {
            TimeUnitRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TimeUnit>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<TimeUnit>()).Returns(TimeUnitRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();

        public UpdateTimeUnitCommandHandler CreateHandler()
        {
            return new UpdateTimeUnitCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
