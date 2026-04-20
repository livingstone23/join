using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TimeUnits.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TimeUnits.Commands.DeleteTimeUnit;

/// <summary>
/// Contains the unit tests for the time unit delete command.
/// These tests verify not-found handling, dependency protection, and soft-delete behavior.
/// </summary>
public sealed class DeleteTimeUnitCommandHandlerTests
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
        var context = new DeleteTimeUnitCommandTestContext();
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync((TimeUnit?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTimeUnitCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the protected branch when the time unit is still used by active tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityIsUsedByTickets_ShouldReturnInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            new Ticket { TimeUnitId = entityId, GcRecord = BaseAuditableEntity.ActiveGcRecord }
        ]);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTimeUnitCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_IN_USE");
    }

    /// <summary>
    /// Verifies the protected branch when the time unit is still used by active ticket complexities.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityIsUsedByTicketComplexities_ShouldReturnInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            new TicketComplexity { TimeUnitId = entityId, GcRecord = BaseAuditableEntity.ActiveGcRecord }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTimeUnitCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_IN_USE");
        response.Errors.Should().Contain("The time unit is currently linked to tickets or ticket complexities and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTimeUnitCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the entity can be soft deleted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityIsNotInUse_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTimeUnitCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketComplexity>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTimeUnitCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Time unit deleted successfully.");
        response.Data.Should().Be(entityId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.TimeUnitRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates an existing time unit entity for delete scenarios.
    /// </summary>
    private static TimeUnit CreateExistingEntity(Guid id)
    {
        var entity = new TimeUnit
        {
            CompanyId = Guid.NewGuid(),
            Name = "Hours",
            Code = 1,
            IsActive = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(entity, id);

        return entity;
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteTimeUnitCommandTestContext
    {
        public DeleteTimeUnitCommandTestContext()
        {
            TimeUnitRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TimeUnit>())).ReturnsAsync(true);
            UnitOfWorkMock.Setup(x => x.GetRepository<TimeUnit>()).Returns(TimeUnitRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketComplexity>()).Returns(TicketComplexityRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> TicketComplexityRepositoryMock { get; } = new();

        public DeleteTimeUnitCommandHandler CreateHandler()
        {
            return new DeleteTimeUnitCommandHandler(UnitOfWorkMock.Object);
        }
    }
}
