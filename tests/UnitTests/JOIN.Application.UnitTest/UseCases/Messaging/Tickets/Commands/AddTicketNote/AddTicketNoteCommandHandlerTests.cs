using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.AddTicketNote;

/// <summary>
/// Unit tests for <see cref="AddTicketNoteCommandHandler"/>. Covers the SPEC 35
/// gate sequence: COMPANY_REQUIRED → USER_REQUIRED → INVALID_LOG_TYPE →
/// TICKET_NOT_FOUND → happy path. Also verifies that visibility is derived
/// server-side from the <see cref="LogType"/> (InternalNote ⇒ private,
/// ExternalNote ⇒ public).
/// </summary>
public sealed class AddTicketNoteCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant claim is missing from the token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new AddTicketNoteTestContext(Guid.Empty, _fixture.Create<Guid>());

        // Act
        var response = await context.CreateHandler().Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the early exit when the user id claim is not a parseable GUID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequiredError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new AddTicketNoteTestContext(companyId, Guid.Empty);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");

        // Act
        var response = await context.CreateHandler().Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the 400 guard when the supplied <see cref="LogType"/> is one
    /// of the system-generated types (here, <c>Creation</c>) — only
    /// <c>InternalNote</c> and <c>ExternalNote</c> are accepted from the user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLogTypeIsCreation_ShouldReturnInvalidLogTypeError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var context = new AddTicketNoteTestContext(companyId, currentUserId);

        // Act
        var response = await context.CreateHandler().Handle(
            CreateValidCommand(LogType.Creation),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_LOG_TYPE");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the 404 branch when the ticket id does not resolve to a row in
    /// the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketNotFound_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new AddTicketNoteTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync((Ticket?)null);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies that an <c>InternalNote</c> appends a log row with
    /// <c>IsOnlyForCreatedAndAssigned = true</c> — visibility is derived from
    /// the <see cref="LogType"/> so internal notes stay hidden from third viewers.
    /// </summary>
    [Fact]
    public async Task Handle_WhenInternalNote_ShouldPersistIsOnlyForCreatedAndAssignedTrue()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(LogType.InternalNote);

        var entity = CreateTicket(companyId);

        var context = new AddTicketNoteTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Note added successfully.");

        var log = entity.TicketLogs.Single();
        log.LogType.Should().Be(LogType.InternalNote);
        log.IsOnlyForCreatedAndAssigned.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an <c>ExternalNote</c> appends a log row with
    /// <c>IsOnlyForCreatedAndAssigned = false</c> — public notes are visible to
    /// every viewer with read access on the ticket.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExternalNote_ShouldPersistIsOnlyForCreatedAndAssignedFalse()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(LogType.ExternalNote);

        var entity = CreateTicket(companyId);

        var context = new AddTicketNoteTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();

        var log = entity.TicketLogs.Single();
        log.LogType.Should().Be(LogType.ExternalNote);
        log.IsOnlyForCreatedAndAssigned.Should().BeFalse();
    }

    /// <summary>
    /// Verifies the happy path: the handler returns a <c>Response&lt;TicketLogDto&gt;</c>
    /// with the projected <see cref="LogType"/> string, the actor's display name,
    /// the user-supplied summary, and the timestamp recorded by <c>AddLog</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllGatesPass_ShouldReturnTicketLogDtoWithExpectedFields()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(LogType.InternalNote) with { Summary = "Customer escalated by phone." };

        var entity = CreateTicket(companyId);

        var context = new AddTicketNoteTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Note added successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.LogType.Should().Be(nameof(LogType.InternalNote));
        response.Data.Summary.Should().Be("Customer escalated by phone.");
        response.Data.UserRegisteredName.Should().Be("Ana Torres");
        response.Data.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));

        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private AddTicketNoteCommand CreateValidCommand(LogType logType = LogType.InternalNote) => new()
    {
        TicketId = _fixture.Create<Guid>(),
        LogType = logType,
        Summary = "Internal note for the test."
    };

    private static Ticket CreateTicket(Guid companyId) => new()
    {
        CompanyId = companyId,
        Name = "T",
        Description = "D",
        EstimatedTime = 1m,
        ConsumedTime = 0m,
        TicketStatusId = Guid.NewGuid(),
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        CreatedByUserId = Guid.NewGuid()
    };

    /// <summary>
    /// Wires the repositories the handler touches. The assembler / resolver are
    /// not part of the AddTicketNote flow, so this context is intentionally lean.
    /// </summary>
    private sealed class AddTicketNoteTestContext
    {
        public AddTicketNoteTestContext(Guid companyId, Guid currentUserId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();

        public AddTicketNoteCommandHandler CreateHandler()
        {
            return new AddTicketNoteCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }

        private static void SetupRepository<TEntity>(
            Mock<IUnitOfWork> unitOfWorkMock,
            Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}