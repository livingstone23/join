using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.UpdateTicket;

/// <summary>
/// Contains the unit tests for the ticket update flow.
/// The suite focuses on the happy path and the highest-risk error branches
/// to maximize meaningful coverage of the handler.
/// </summary>
public sealed class UpdateTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path for a valid update request.
    /// This test ensures the ticket is updated, persisted, and enriched in the response,
    /// while also validating that status and assignment logs are registered.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdateTicketAndReturnSuccessResponse()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var assignedUserId = _fixture.Create<Guid>();

        if (assignedUserId == currentUserId)
        {
            assignedUserId = Guid.NewGuid();
        }

        var request = CreateValidCommand(
            id: _fixture.Create<Guid>(),
            assignedToUserId: assignedUserId);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId,
            originalStatusId: Guid.NewGuid(),
            originalAssignedToUserId: null);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress" });

        context.ComplexityRepositoryMock
            .Setup(x => x.GetAsync(request.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });

        context.TimeUnitRepositoryMock
            .Setup(x => x.GetAsync(request.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });

        context.ChannelRepositoryMock
            .Setup(x => x.GetAsync(request.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal Web" });

        context.UserRepositoryMock
            .Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        context.UserRepositoryMock
            .Setup(x => x.GetAsync(assignedUserId))
            .ReturnsAsync(new ApplicationUser { Id = assignedUserId, FirstName = "Luis", LastName = "Gomez" });

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new UserCompany
                {
                    CompanyId = companyId,
                    UserId = assignedUserId,
                    IsDefault = true
                }
            });

        context.TicketRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<Ticket>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, entity))
            .Callback(() => ApplyRequestToTicket(request, entity));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket updated successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be("JOIN");
        response.Data.Name.Should().Be(request.Name);
        response.Data.Description.Should().Be(request.Description);
        response.Data.AssignedToUserId.Should().Be(assignedUserId);
        response.Data.AssignedToUserName.Should().Be("Luis Gomez");
        response.Data.ChannelName.Should().Be("Portal Web");
        response.Data.TicketStatusId.Should().Be(request.TicketStatusId);

        entity.EffortPoints.Should().Be(request.EffortPoints);
        entity.TicketLogs.Should().HaveCount(2);
        entity.TicketLogs.Should().ContainSingle(x => x.LogType == LogType.StatusChange);
        entity.TicketLogs.Should().ContainSingle(x => x.LogType == LogType.Reassignment);

        context.MapperMock.Verify(x => x.ApplyUpdate(request, entity), Times.Once);
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the current tenant is missing.
    /// This protects the multi-tenant boundary before any repository access occurs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();

        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        currentUserServiceMock.SetupGet(x => x.UserId).Returns(_fixture.Create<Guid>().ToString());

        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var handler = new UpdateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");

        mapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
        unitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
    }

    /// <summary>
    /// Verifies the early exit when the authenticated user identifier is invalid.
    /// This ensures the audit context is valid before performing any update work.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequiredError()
    {
        // Arrange
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();

        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
        currentUserServiceMock.SetupGet(x => x.UserId).Returns("invalid-guid");

        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var handler = new UpdateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");

        mapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
        unitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when the ticket does not belong to the current company.
    /// This test protects tenant isolation by preventing cross-company updates.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketDoesNotBelongToCurrentCompany_ShouldReturnTicketNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(CreateExistingTicket(
                companyId: Guid.NewGuid(),
                createdByUserId: currentUserId));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");

        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when the assigned user is not linked to the current tenant.
    /// This test protects a critical authorization branch before persistence.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignedUserIsNotLinkedToTenant_ShouldReturnInvalidAssignedUserTenantError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var assignedUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand(
            id: _fixture.Create<Guid>(),
            assignedToUserId: assignedUserId);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress" });

        context.ComplexityRepositoryMock
            .Setup(x => x.GetAsync(request.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });

        context.TimeUnitRepositoryMock
            .Setup(x => x.GetAsync(request.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });

        context.ChannelRepositoryMock
            .Setup(x => x.GetAsync(request.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal Web" });

        context.UserRepositoryMock
            .Setup(x => x.GetAsync(assignedUserId))
            .ReturnsAsync(new ApplicationUser { Id = assignedUserId, FirstName = "Luis", LastName = "Gomez" });

        context.UserCompanyRepositoryMock
            .Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<UserCompany>());

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER_TENANT");

        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when a ticket references itself as precedent.
    /// This test protects a business rule that prevents invalid circular relationships.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketReferencesItselfAsPrecedent_ShouldReturnInvalidPrecedentTicketError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();

        var request = CreateValidCommand(
            id: ticketId,
            precedentTicketId: ticketId);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(ticketId))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress" });

        context.ComplexityRepositoryMock
            .Setup(x => x.GetAsync(request.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });

        context.TimeUnitRepositoryMock
            .Setup(x => x.GetAsync(request.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });

        context.ChannelRepositoryMock
            .Setup(x => x.GetAsync(request.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal Web" });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_PRECEDENT_TICKET");

        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are affected.
    /// This scenario also confirms that no audit logs are added when neither
    /// the status nor the assignment changed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var statusId = _fixture.Create<Guid>();

        var request = CreateValidCommand(
            id: _fixture.Create<Guid>(),
            ticketStatusId: statusId,
            assignedToUserId: null);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId,
            originalStatusId: statusId,
            originalAssignedToUserId: null);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress" });

        context.ComplexityRepositoryMock
            .Setup(x => x.GetAsync(request.TicketComplexityId))
            .ReturnsAsync(new TicketComplexity { Name = "High" });

        context.TimeUnitRepositoryMock
            .Setup(x => x.GetAsync(request.TimeUnitId))
            .ReturnsAsync(new TimeUnit { Name = "Hours" });

        context.ChannelRepositoryMock
            .Setup(x => x.GetAsync(request.ChannelId))
            .ReturnsAsync(new CommunicationChannel { Name = "Portal Web" });

        context.TicketRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<Ticket>()))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, entity))
            .Callback(() => ApplyRequestToTicket(request, entity));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");
        entity.TicketLogs.Should().BeEmpty();

        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid and controlled command for reuse across multiple tests.
    /// The method keeps the setup explicit so each test can focus on the branch it wants to validate.
    /// </summary>
    private UpdateTicketCommand CreateValidCommand(
        Guid id,
        Guid? assignedToUserId = null,
        Guid? precedentTicketId = null,
        Guid? ticketStatusId = null)
    {
        return _fixture.Build<UpdateTicketCommand>()
            .With(x => x.Id, id)
            .With(x => x.Name, "Updated ticket")
            .With(x => x.Description, "Updated description")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 3m)
            .With(x => x.EffortPoints, 5m)
            .With(x => x.IsVisibleToExternals, true)
            .With(x => x.TicketStatusId, ticketStatusId ?? _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.CustomerId, (Guid?)null)
            .With(x => x.ProjectId, (Guid?)null)
            .With(x => x.AreaId, (Guid?)null)
            .With(x => x.AssignedToUserId, assignedToUserId)
            .With(x => x.PrecedentTicketId, precedentTicketId)
            .Create();
    }

    /// <summary>
    /// Builds an existing ticket entity used as the update target.
    /// This keeps the tests focused on the handler behavior instead of entity construction noise.
    /// </summary>
    private static Ticket CreateExistingTicket(
        Guid companyId,
        Guid createdByUserId,
        Guid? originalStatusId = null,
        Guid? originalAssignedToUserId = null)
    {
        var entity = new Ticket
        {
            CompanyId = companyId,
            CreatedByUserId = createdByUserId,
            Name = "Original ticket",
            Description = "Original description",
            EstimatedTime = 5m,
            ConsumedTime = 1m,
            EffortPoints = 2m,
            IsVisibleToExternals = false,
            TicketStatusId = originalStatusId ?? Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            AssignedToUserId = originalAssignedToUserId,
            Created = DateTime.UtcNow.AddDays(-1)
        };

        entity.SetStandardCode(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        return entity;
    }

    /// <summary>
    /// Applies the request values to the existing ticket entity.
    /// The callback emulates the real mapper behavior while keeping the test isolated to the handler.
    /// </summary>
    private static void ApplyRequestToTicket(UpdateTicketCommand request, Ticket entity)
    {
        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.EstimatedTime = request.EstimatedTime;
        entity.ConsumedTime = request.ConsumedTime;
        entity.EffortPoints = request.EffortPoints;
        entity.IsVisibleToExternals = request.IsVisibleToExternals;
        entity.TicketStatusId = request.TicketStatusId;
        entity.TicketComplexityId = request.TicketComplexityId;
        entity.TimeUnitId = request.TimeUnitId;
        entity.CustomerId = request.CustomerId;
        entity.ProjectId = request.ProjectId;
        entity.AreaId = request.AreaId;
        entity.ChannelId = request.ChannelId;
        entity.AssignedToUserId = request.AssignedToUserId;
        entity.PrecedentTicketId = request.PrecedentTicketId;
    }

    /// <summary>
    /// Creates the reusable mocked context for the handler and registers all repository lookups.
    /// This keeps the tests readable and aligned with the generic repository pattern used in production.
    /// </summary>
    private static UpdateTicketTestContext CreateContext(Guid companyId, Guid currentUserId)
    {
        return new UpdateTicketTestContext(companyId, currentUserId);
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise in each test.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>() where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers each repository in the mocked unit of work.
    /// This helper mirrors the generic resolution approach used by the application layer.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks needed by the update handler tests.
    /// </summary>
    private sealed class UpdateTicketTestContext
    {
        public UpdateTicketTestContext(Guid companyId, Guid currentUserId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, ComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            SetupRepository(UnitOfWorkMock, ChannelRepositoryMock);
            SetupRepository(UnitOfWorkMock, CustomerRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ITicketMapper> MapperMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = CreateRepositoryMock<Company>();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = CreateRepositoryMock<Ticket>();
        public Mock<IGenericRepository<TicketStatus>> StatusRepositoryMock { get; } = CreateRepositoryMock<TicketStatus>();
        public Mock<IGenericRepository<TicketComplexity>> ComplexityRepositoryMock { get; } = CreateRepositoryMock<TicketComplexity>();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = CreateRepositoryMock<TimeUnit>();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = CreateRepositoryMock<CommunicationChannel>();
        public Mock<IGenericRepository<Customer>> CustomerRepositoryMock { get; } = CreateRepositoryMock<Customer>();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = CreateRepositoryMock<Project>();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = CreateRepositoryMock<Area>();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = CreateRepositoryMock<ApplicationUser>();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = CreateRepositoryMock<UserCompany>();

        public UpdateTicketCommandHandler CreateHandler()
        {
            return new UpdateTicketCommandHandler(
                UnitOfWorkMock.Object,
                MapperMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
