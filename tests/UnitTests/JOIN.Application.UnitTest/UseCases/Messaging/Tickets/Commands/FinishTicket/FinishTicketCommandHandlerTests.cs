using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.FinishTicket;

/// <summary>
/// Unit tests for <see cref="FinishTicketCommandHandler"/>. Covers the SPEC 35
/// gate sequence: COMPANY_REQUIRED → USER_REQUIRED → TICKET_NOT_FOUND →
/// TICKET_FINISH_FORBIDDEN → INVALID_TICKET_STATUS → TICKET_STATUS_NOT_FINAL →
/// TICKET_ALREADY_FINISHED → happy path. Also verifies the super-admin bypass
/// of <c>CanFinishTicket</c>.
/// </summary>
public sealed class FinishTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant claim is missing from the token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new FinishTicketTestContext(Guid.Empty, _fixture.Create<Guid>());

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
        var context = new FinishTicketTestContext(companyId, Guid.Empty);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");

        // Act
        var response = await context.CreateHandler().Handle(CreateValidCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the ticket id does not resolve to a row
    /// in the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketNotFound_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync((Ticket?)null);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the 403 gate when the actor lacks both <c>CanFinishTicket</c>
    /// and <c>IsSuperAdminTicket</c> for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActorLacksBothCapabilities_ShouldReturnTicketFinishForbiddenError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, currentStatusId: Guid.NewGuid()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketUserCompany>());

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_FINISH_FORBIDDEN");
        context.StatusRepositoryMock.Verify(x => x.GetAsync(request.TicketStatusId), Times.Never);
    }

    /// <summary>
    /// Verifies the super-admin bypass (SPEC 35 decision): an actor with
    /// <c>IsSuperAdminTicket = true</c> can finalize a ticket even without the
    /// <c>CanFinishTicket</c> flag — otherwise super-admins would have a smaller
    /// authority than their role implies.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActorIsSuperAdminWithoutCanFinish_ShouldAllowFinalization()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var previousStatusId = Guid.NewGuid();
        var request = CreateValidCommand();
        var entity = CreateTicket(companyId, currentStatusId: previousStatusId);

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, isSuperAdmin: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Closed", IsFinal = true });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(previousStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Open", IsFinal = false });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(entity.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entity.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(entity.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket finalized successfully.");
        entity.TicketStatusId.Should().Be(request.TicketStatusId);
        entity.TicketLogs.Should().ContainSingle(x => x.LogType == LogType.Finalization);
    }

    /// <summary>
    /// Verifies the 400 guard when the supplied target <c>TicketStatusId</c> does
    /// not resolve to a real status row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetStatusNotFound_ShouldReturnInvalidTicketStatusError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, currentStatusId: Guid.NewGuid()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, canFinish: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync((TicketStatus?)null);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_STATUS");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the 400 guard when the target status exists but is not marked
    /// <c>IsFinal = true</c> — the regular update flow must be used instead.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetStatusIsNotFinal_ShouldReturnTicketStatusNotFinalError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, currentStatusId: Guid.NewGuid()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, canFinish: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress", IsFinal = false });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_NOT_FINAL");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the 409 guard when the ticket is already in a final status —
    /// <c>FinishTicket</c> is one-way and cannot re-finalize or pivot to another
    /// final status (SPEC 35 decision).
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketAlreadyFinalized_ShouldReturnTicketAlreadyFinishedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var currentStatusId = Guid.NewGuid();
        var request = CreateValidCommand();

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, currentStatusId: currentStatusId));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, canFinish: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Closed", IsFinal = true });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(currentStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Resolved", IsFinal = true });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ALREADY_FINISHED");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: actor can finish, target is final, ticket is not
    /// already finalized — the handler persists the new status and appends a
    /// <c>LogType.Finalization</c> entry with the correct <c>previousStatusId</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllGatesPass_ShouldUpdateStatusAndAppendFinalizationLog()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var previousStatusId = Guid.NewGuid();
        var request = CreateValidCommand() with { ResolutionSummary = "Customer confirmed fix." };

        var entity = CreateTicket(companyId, currentStatusId: previousStatusId);

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, canFinish: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Closed", IsFinal = true });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(previousStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Open", IsFinal = false });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(entity.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entity.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(entity.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket finalized successfully.");
        response.Data!.TicketStatusId.Should().Be(request.TicketStatusId);

        entity.TicketStatusId.Should().Be(request.TicketStatusId);
        entity.LastModifiedBy.Should().Be(currentUserId.ToString());

        var log = entity.TicketLogs.Single(x => x.LogType == LogType.Finalization);
        log.PreviousStatusId.Should().Be(previousStatusId);
        log.Summary.Should().Be("Customer confirmed fix.");

        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that an empty/whitespace <c>ResolutionSummary</c> is replaced by
    /// the default "Ticket finalizado" string before the log is appended.
    /// </summary>
    [Fact]
    public async Task Handle_WhenResolutionSummaryIsBlank_ShouldDefaultToTicketFinalizado()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var previousStatusId = Guid.NewGuid();
        var request = CreateValidCommand() with { ResolutionSummary = "   " };

        var entity = CreateTicket(companyId, currentStatusId: previousStatusId);

        var context = new FinishTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, canFinish: true) });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Closed", IsFinal = true });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(previousStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Open", IsFinal = false });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(entity.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(entity.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(entity.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal" });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        entity.TicketLogs.Single(x => x.LogType == LogType.Finalization).Summary.Should().Be("Ticket finalizado");
    }

    private FinishTicketCommand CreateValidCommand() => new()
    {
        TicketId = _fixture.Create<Guid>(),
        TicketStatusId = _fixture.Create<Guid>(),
        ResolutionSummary = "Done."
    };

    private static Ticket CreateTicket(Guid companyId, Guid currentStatusId) => new()
    {
        CompanyId = companyId,
        Name = "T",
        Description = "D",
        EstimatedTime = 1m,
        ConsumedTime = 0m,
        TicketStatusId = currentStatusId,
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        CreatedByUserId = Guid.NewGuid()
    };

    private static TicketUserCompany CreateRosterRow(
        Guid userId,
        Guid companyId,
        bool isSuperAdmin = false,
        bool canFinish = false,
        bool canResolve = false) => new()
        {
            CompanyId = companyId,
            UserId = userId,
            IsSuperAdminTicket = isSuperAdmin,
            CanFinishTicket = canFinish,
            CanResolveTicket = canResolve,
            GcRecord = 0
        };

    /// <summary>
    /// Wires every repository the handler (or its assembler/resolver dependencies)
    /// touches, so individual tests only need to set up the specific mocks they
    /// exercise. Keeps each test focused on the branch it wants to validate.
    /// </summary>
    private sealed class FinishTicketTestContext
    {
        public FinishTicketTestContext(Guid companyId, Guid currentUserId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketUserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, ComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            SetupRepository(UnitOfWorkMock, ChannelRepositoryMock);
            SetupRepository(UnitOfWorkMock, PersonRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> ComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();

        public FinishTicketCommandHandler CreateHandler()
        {
            return new FinishTicketCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new TicketUserCompanyCapabilityResolver(UnitOfWorkMock.Object),
                new TicketDtoAssembler(UnitOfWorkMock.Object));
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