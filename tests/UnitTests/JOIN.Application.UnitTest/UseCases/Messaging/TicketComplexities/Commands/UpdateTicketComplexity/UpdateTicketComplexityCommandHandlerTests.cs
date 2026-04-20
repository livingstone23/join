using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Commands.UpdateTicketComplexity;

/// <summary>
/// Contains the unit tests for the ticket complexity update command.
/// These tests verify not-found handling, duplicate validation, time-unit validation, and successful persistence.
/// </summary>
public sealed class UpdateTicketComplexityCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the not-found branch when the requested entity does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new UpdateTicketComplexityCommandTestContext();
        var entityId = _fixture.Create<Guid>();
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync((TicketComplexity?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the duplicate-name branch while excluding the entity being updated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherEntityUsesTheSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            entity,
            CreateExistingEntity(_fixture.Create<Guid>(), name: "Critical", code: 90)
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, name: "  critical  "), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code branch while excluding the entity being updated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherEntityUsesTheSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            entity,
            CreateExistingEntity(_fixture.Create<Guid>(), name: "Other", code: 77)
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(entityId, code: 77), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the related time-unit validation during update.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeUnitDoesNotExist_ShouldReturnTimeUnitNotFoundError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);
        var command = CreateValidCommand(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync((TimeUnit?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TIME_UNIT_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the update is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);
        var command = CreateValidCommand(entityId, name: "  Updated  ", description: "  Normalized  ");

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync(new TimeUnit());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        entity.Name.Should().Be("Updated");
        entity.Description.Should().Be("Normalized");
    }

    /// <summary>
    /// Verifies the successful update flow and normalized values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateEntityAndReturnSuccess()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new UpdateTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);
        var command = CreateValidCommand(entityId, name: "  Updated  ", description: "  Higher effort  ", code: 45, resolutionTimeUnits: 6, isActive: false);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(command.TimeUnitId)).ReturnsAsync(new TimeUnit());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(command, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket complexity updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(entityId);
        response.Data.Name.Should().Be("Updated");
        response.Data.Description.Should().Be("Higher effort");
        response.Data.Code.Should().Be(45);
        response.Data.ResolutionTimeUnits.Should().Be(6);
        response.Data.IsActive.Should().BeFalse();
        context.TicketComplexityRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command for ticket complexity scenarios.
    /// </summary>
    private UpdateTicketComplexityCommand CreateValidCommand(
        Guid id,
        string name = "Standard",
        string? description = "Default description",
        int code = 10,
        int resolutionTimeUnits = 2,
        bool isActive = true)
    {
        return new UpdateTicketComplexityCommand
        {
            Id = id,
            Name = name,
            Description = description,
            Code = code,
            ResolutionTimeUnits = resolutionTimeUnits,
            TimeUnitId = _fixture.Create<Guid>(),
            IsActive = isActive
        };
    }

    /// <summary>
    /// Creates a reusable ticket complexity entity for update scenarios.
    /// </summary>
    private static TicketComplexity CreateExistingEntity(Guid id, string name = "Standard", int code = 10)
    {
        var entity = new TicketComplexity
        {
            CompanyId = Guid.NewGuid(),
            Name = name,
            Description = "Existing description",
            Code = code,
            ResolutionTimeUnits = 2,
            TimeUnitId = Guid.NewGuid(),
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
    private sealed class UpdateTicketComplexityCommandTestContext
    {
        public UpdateTicketComplexityCommandTestContext()
        {
            TicketComplexityRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketComplexity>())).ReturnsAsync(true);
            SetupRepository(UnitOfWorkMock, TicketComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> TicketComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();

        public UpdateTicketComplexityCommandHandler CreateHandler()
        {
            return new UpdateTicketComplexityCommandHandler(UnitOfWorkMock.Object);
        }

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
