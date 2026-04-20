using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Commands.DeleteTicketComplexity;

/// <summary>
/// Contains the unit tests for the ticket complexity delete command.
/// These tests verify not-found handling, in-use protection, and soft-delete behavior.
/// </summary>
public sealed class DeleteTicketComplexityCommandHandlerTests
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
        var context = new DeleteTicketComplexityCommandTestContext();
        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync((TicketComplexity?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketComplexityCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the protected branch when the complexity is still linked to active tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenEntityIsInUse_ShouldReturnInUseError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(
        [
            new Ticket { TicketComplexityId = entityId, GcRecord = BaseAuditableEntity.ActiveGcRecord }
        ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketComplexityCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_COMPLEXITY_IN_USE");
        response.Errors.Should().Contain("The ticket complexity is currently linked to active tickets and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the soft delete is not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var entityId = _fixture.Create<Guid>();
        var context = new DeleteTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketComplexityCommand(entityId), CancellationToken.None);

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
        var context = new DeleteTicketComplexityCommandTestContext();
        var entity = CreateExistingEntity(entityId);

        context.TicketComplexityRepositoryMock.Setup(x => x.GetAsync(entityId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketComplexityCommand(entityId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket complexity deleted successfully.");
        response.Data.Should().Be(entityId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.TicketComplexityRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates an existing ticket complexity entity for delete scenarios.
    /// </summary>
    private static TicketComplexity CreateExistingEntity(Guid id)
    {
        var entity = new TicketComplexity
        {
            CompanyId = Guid.NewGuid(),
            Name = "Standard",
            Description = "Default",
            Code = 10,
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
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteTicketComplexityCommandTestContext
    {
        public DeleteTicketComplexityCommandTestContext()
        {
            TicketComplexityRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketComplexity>())).ReturnsAsync(true);
            SetupRepository(UnitOfWorkMock, TicketComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> TicketComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();

        public DeleteTicketComplexityCommandHandler CreateHandler()
        {
            return new DeleteTicketComplexityCommandHandler(UnitOfWorkMock.Object);
        }

        private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}
