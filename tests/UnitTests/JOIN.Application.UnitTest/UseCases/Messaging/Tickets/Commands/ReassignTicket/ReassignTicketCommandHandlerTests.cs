using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.ReassignTicket;

/// <summary>
/// Unit tests for <see cref="ReassignTicketCommandHandler"/>. Covers the SPEC 35
/// gate sequence: COMPANY_REQUIRED → USER_REQUIRED → TICKET_NOT_FOUND →
/// TICKET_REASSIGN_FORBIDDEN → INVALID_ASSIGNED_USER → INVALID_ASSIGNED_USER_TENANT
/// → TARGET_NOT_ELIGIBLE_RESOLVER → no-op / happy path.
/// </summary>
public sealed class ReassignTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant claim is missing from the token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new ReassignTicketTestContext(Guid.Empty, _fixture.Create<Guid>());

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
        var context = new ReassignTicketTestContext(companyId, Guid.Empty);
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
    /// in the current tenant — the gate returns 404 before any capability lookup runs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketNotFound_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync((Ticket?)null);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the cross-tenant branch — a ticket that belongs to a different
    /// company must surface as 404, never 200 or 403 (tenant isolation).
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketBelongsToDifferentTenant_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(Guid.NewGuid(), assignedTo: _fixture.Create<Guid>()));

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
    }

    /// <summary>
    /// Verifies the 403 gate when the actor lacks <c>IsSuperAdminTicket</c> for
    /// the current tenant — only super-admins can redistribute tickets.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActorIsNotSuperAdmin_ShouldReturnTicketReassignForbiddenError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, assignedTo: _fixture.Create<Guid>()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketUserCompany>()); // no roster row ⇒ no flags

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_REASSIGN_FORBIDDEN");
        context.UserRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// Verifies the 400 guard when the destination user id does not resolve to
    /// a real <c>ApplicationUser</c> row.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetUserNotFound_ShouldReturnInvalidAssignedUserError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, assignedTo: _fixture.Create<Guid>()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, isSuperAdmin: true) });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId)).ReturnsAsync((ApplicationUser?)null);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER");
    }

    /// <summary>
    /// Verifies the 400 guard when the destination user is not linked to the
    /// current tenant via <c>UserCompany</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetUserNotInTenant_ShouldReturnInvalidAssignedUserTenantError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, assignedTo: _fixture.Create<Guid>()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateRosterRow(currentUserId, companyId, isSuperAdmin: true) });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId))
            .ReturnsAsync(new ApplicationUser { Id = targetUserId, FirstName = "T", LastName = "U" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>()); // target has no tenant link

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER_TENANT");
    }

    /// <summary>
    /// Verifies the 400 guard when the destination user lacks the
    /// <c>CanResolveTicket</c> capability for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetIsNotResolver_ShouldReturnTargetNotEligibleResolverError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId))
            .ReturnsAsync(CreateTicket(companyId, assignedTo: _fixture.Create<Guid>()));
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                CreateRosterRow(currentUserId, companyId, isSuperAdmin: true),
                CreateRosterRow(targetUserId, companyId, canResolve: false)
            });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId))
            .ReturnsAsync(new ApplicationUser { Id = targetUserId, FirstName = "T", LastName = "U" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateUserCompany(targetUserId, companyId) });

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TARGET_NOT_ELIGIBLE_RESOLVER");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the no-op branch: when the destination user is already the
    /// assignee, the handler returns success without persisting or emitting a new
    /// <c>Reassignment</c> log entry (SPEC 35 step 8). Target capability is still
    /// validated to avoid letting a stale assignee lock the ticket silently.
    /// </summary>
    [Fact]
    public async Task Handle_WhenReassigningToSameUser_ShouldReturnSuccessWithoutNewLog()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var entity = CreateTicket(companyId, assignedTo: targetUserId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                CreateRosterRow(currentUserId, companyId, isSuperAdmin: true),
                CreateRosterRow(targetUserId, companyId, canResolve: true)
            });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId))
            .ReturnsAsync(new ApplicationUser { Id = targetUserId, FirstName = "T", LastName = "U" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateUserCompany(targetUserId, companyId) });
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN" });
        // Assembler lookups — provide enough so BuildAsync does not NRE.
        context.StatusRepositoryMock.Setup(x => x.GetAsync(entity.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Open" });
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
        response.Message.Should().Be("Ticket reassigned successfully.");
        entity.TicketLogs.Should().NotContain(x => x.LogType == LogType.Reassignment);
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path: actor is a ticket super-admin, target is a roster
    /// resolver, the new assignee differs from the current one, and the
    /// <c>Reassignment</c> log entry is appended with the correct metadata.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAllGatesPass_ShouldUpdateAssigneeAndAppendReassignmentLog()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var previousAssigneeId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var entity = CreateTicket(companyId, assignedTo: previousAssigneeId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                CreateRosterRow(currentUserId, companyId, isSuperAdmin: true),
                CreateRosterRow(targetUserId, companyId, canResolve: true)
            });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId))
            .ReturnsAsync(new ApplicationUser { Id = targetUserId, FirstName = "T", LastName = "U" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateUserCompany(targetUserId, companyId) });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN" });
        context.StatusRepositoryMock.Setup(x => x.GetAsync(entity.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Open" });
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
        response.Message.Should().Be("Ticket reassigned successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.AssignedToUserId.Should().Be(targetUserId);

        entity.AssignedToUserId.Should().Be(targetUserId);
        entity.LastModifiedBy.Should().Be(currentUserId.ToString());

        var log = entity.TicketLogs.Single(x => x.LogType == LogType.Reassignment);
        log.NewAssignedToUserId.Should().Be(targetUserId);
        log.Summary.Should().Be("Ticket reasignado");

        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies that a save affecting no rows surfaces <c>REASSIGN_FAILED</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoRowsAffected_ShouldReturnReassignFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var targetUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(targetUserId);

        var entity = CreateTicket(companyId);

        var context = new ReassignTicketTestContext(companyId, currentUserId);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.TicketId)).ReturnsAsync(entity);
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                CreateRosterRow(currentUserId, companyId, isSuperAdmin: true),
                CreateRosterRow(targetUserId, companyId, canResolve: true)
            });
        context.UserRepositoryMock.Setup(x => x.GetAsync(targetUserId))
            .ReturnsAsync(new ApplicationUser { Id = targetUserId, FirstName = "T", LastName = "U" });
        context.UserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { CreateUserCompany(targetUserId, companyId) });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        // Act
        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("REASSIGN_FAILED");
    }

    private ReassignTicketCommand CreateValidCommand(Guid? newAssignedToUserId = null) => new()
    {
        TicketId = _fixture.Create<Guid>(),
        NewAssignedToUserId = newAssignedToUserId ?? _fixture.Create<Guid>()
    };

    private static Ticket CreateTicket(Guid companyId, Guid? assignedTo = null)
    {
        var ticket = new Ticket
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
            CreatedByUserId = Guid.NewGuid(),
            AssignedToUserId = assignedTo
        };
        ticket.SetStandardCode(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        return ticket;
    }

    private static TicketUserCompany CreateRosterRow(
        Guid userId,
        Guid companyId,
        bool isSuperAdmin = false,
        bool canFinish = false,
        bool canResolve = false,
        int gcRecord = 0) => new()
        {
            CompanyId = companyId,
            UserId = userId,
            IsSuperAdminTicket = isSuperAdmin,
            CanFinishTicket = canFinish,
            CanResolveTicket = canResolve,
            GcRecord = gcRecord
        };

    private static UserCompany CreateUserCompany(Guid userId, Guid companyId, int gcRecord = 0) => new()
    {
        UserId = userId,
        CompanyId = companyId,
        GcRecord = gcRecord
    };

    /// <summary>
    /// Wires every repository the handler (or its assembler/resolver dependencies)
    /// touches, so individual tests only need to set up the specific mocks they
    /// exercise. Keeps each test focused on the branch it wants to validate.
    /// </summary>
    private sealed class ReassignTicketTestContext
    {
        public ReassignTicketTestContext(Guid companyId, Guid currentUserId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketUserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, ComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            SetupRepository(UnitOfWorkMock, PersonRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, ChannelRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> ComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = new();

        public ReassignTicketCommandHandler CreateHandler()
        {
            return new ReassignTicketCommandHandler(
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