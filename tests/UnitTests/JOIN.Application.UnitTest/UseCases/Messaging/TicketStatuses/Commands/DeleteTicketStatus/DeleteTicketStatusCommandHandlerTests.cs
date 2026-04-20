using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Commands.DeleteTicketStatus;

/// <summary>
/// Contains the unit tests for the ticket status delete command.
/// These tests verify tenant protection, not-found handling, in-use protection,
/// soft-delete behavior, and the persistence failure path.
/// </summary>
public sealed class DeleteTicketStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new DeleteTicketStatusCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketStatusCommand(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
    }

    /// <summary>
    /// Verifies the not-found branch when the status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new DeleteTicketStatusCommandTestContext(companyId);
        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync((TicketStatus?)null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketStatusCommand(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("Ticket status not found.");
    }

    /// <summary>
    /// Verifies the protected branch when the ticket status is still linked to active tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusIsInUse_ShouldReturnInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new DeleteTicketStatusCommandTestContext(companyId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                new Ticket { TicketStatusId = statusId, GcRecord = BaseAuditableEntity.ActiveGcRecord }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketStatusCommand(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_IN_USE");
        response.Errors.Should().Contain("The ticket status is currently linked to active tickets and cannot be deleted.");
    }

    /// <summary>
    /// Verifies the persistence failure branch when the delete is attempted but not committed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new DeleteTicketStatusCommandTestContext(companyId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketStatusCommand(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the ticket status.");
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Verifies the happy path when the status is not used by active tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusIsNotInUse_ShouldSoftDeleteAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new DeleteTicketStatusCommandTestContext(companyId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeleteTicketStatusCommand(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket status deleted successfully.");
        response.Data.Should().Be(statusId);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.TicketStatusRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    /// <summary>
    /// Creates an existing ticket status entity for delete scenarios.
    /// </summary>
    private static TicketStatus CreateExistingStatus(Guid id, Guid companyId)
    {
        var entity = new TicketStatus
        {
            CompanyId = companyId,
            Name = "Open",
            Description = "Initial state",
            Code = 10,
            IsActive = true,
            IsInitial = true,
            IsPaused = false,
            IsFinal = false
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(entity, id);

        return entity;
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>() where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers a generic repository in the mocked unit of work.
    /// </summary>
    private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the delete handler.
    /// </summary>
    private sealed class DeleteTicketStatusCommandTestContext
    {
        public DeleteTicketStatusCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> TicketStatusRepositoryMock { get; } = CreateRepositoryMock<TicketStatus>();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = CreateRepositoryMock<Ticket>();

        public DeleteTicketStatusCommandHandler CreateHandler()
        {
            return new DeleteTicketStatusCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
