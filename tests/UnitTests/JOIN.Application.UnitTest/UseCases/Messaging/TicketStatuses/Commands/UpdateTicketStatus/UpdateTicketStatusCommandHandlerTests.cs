using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Commands.UpdateTicketStatus;

/// <summary>
/// Contains the unit tests for the ticket status update command.
/// These tests verify tenant protection, not-found handling, duplicate validation,
/// persistence failure, and the successful update flow.
/// </summary>
public sealed class UpdateTicketStatusCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new UpdateTicketStatusCommandTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(CreateValidCommand(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    /// <summary>
    /// Verifies the not-found branch when the requested status does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new UpdateTicketStatusCommandTestContext(companyId);
        var handler = context.CreateHandler();

        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAsync(statusId))
            .ReturnsAsync((TicketStatus?)null);

        // Act
        var response = await handler.Handle(CreateValidCommand(statusId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_NOT_FOUND");
        response.Errors.Should().Contain("Ticket status not found.");
    }

    /// <summary>
    /// Verifies the duplicate-name validation branch excluding the current entity id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStatusUsesSameName_ShouldReturnNameInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new UpdateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand(statusId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketStatusRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(
            [
                entity,
                new TicketStatus { Name = request.Name.Trim().ToUpperInvariant(), Code = 999, GcRecord = 0 }
            ]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Message.Should().Be("TICKET_STATUS_NAME_IN_USE");
    }

    /// <summary>
    /// Verifies the duplicate-code validation branch excluding the current entity id.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAnotherStatusUsesSameCode_ShouldReturnCodeInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new UpdateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand(statusId);
        var entity = CreateExistingStatus(statusId, companyId);

        var conflicting = CreateExistingStatus(_fixture.Create<Guid>(), companyId);
        conflicting.Code = request.Code;
        conflicting.Name = "Different";

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketStatusRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity, conflicting]);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Message.Should().Be("TICKET_STATUS_CODE_IN_USE");
    }

    /// <summary>
    /// Verifies the persistence failure branch when no rows are affected.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new UpdateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand(statusId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketStatusRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Message.Should().Be("UPDATE_FAILED");
        response.Errors.Should().Contain("No records were affected while updating the ticket status.");
    }

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateStatusAndReturnDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();
        var context = new UpdateTicketStatusCommandTestContext(companyId);
        var request = CreateValidCommand(statusId);
        var entity = CreateExistingStatus(statusId, companyId);

        context.TicketStatusRepositoryMock.Setup(x => x.GetAsync(statusId)).ReturnsAsync(entity);
        context.TicketStatusRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket status updated successfully.");
        entity.Name.Should().Be("Resolved");
        entity.Description.Should().Be("Final workflow state");
        entity.Code.Should().Be(20);
        entity.IsFinal.Should().BeTrue();
        response.Data!.CompanyName.Should().Be("JOIN CRM");
    }

    /// <summary>
    /// Creates a valid update command.
    /// </summary>
    private UpdateTicketStatusCommand CreateValidCommand(Guid id)
    {
        return _fixture.Build<UpdateTicketStatusCommand>()
            .With(x => x.Id, id)
            .With(x => x.Name, "  Resolved  ")
            .With(x => x.Description, "  Final workflow state  ")
            .With(x => x.Code, 20)
            .With(x => x.IsActive, true)
            .With(x => x.IsInitial, false)
            .With(x => x.IsPaused, false)
            .With(x => x.IsFinal, true)
            .Create();
    }

    /// <summary>
    /// Creates an existing ticket status entity for update scenarios.
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
    /// Holds the reusable mocks and helper factory for the update handler.
    /// </summary>
    private sealed class UpdateTicketStatusCommandTestContext
    {
        public UpdateTicketStatusCommandTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketStatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> TicketStatusRepositoryMock { get; } = CreateRepositoryMock<TicketStatus>();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = CreateRepositoryMock<Company>();

        public UpdateTicketStatusCommandHandler CreateHandler()
        {
            return new UpdateTicketStatusCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
