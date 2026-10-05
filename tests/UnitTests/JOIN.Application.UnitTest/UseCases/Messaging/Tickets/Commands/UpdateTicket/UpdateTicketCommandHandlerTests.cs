using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
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

        var request = CreateValidCommand(
            id: _fixture.Create<Guid>());

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId,
            originalStatusId: Guid.NewGuid());

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress", CompanyId = companyId });

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
        response.Data.ChannelName.Should().Be("Portal Web");
        response.Data.TicketStatusId.Should().Be(request.TicketStatusId);

        entity.EffortPoints.Should().Be(request.EffortPoints);
        entity.TicketLogs.Should().ContainSingle(x => x.LogType == LogType.StatusChange);
        entity.TicketLogs.Should().NotContain(x => x.LogType == LogType.Reassignment);

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
        var handler = new UpdateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketStatusTransitionGuard(unitOfWorkMock.Object));

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
        var handler = new UpdateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketStatusTransitionGuard(unitOfWorkMock.Object));

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
            .ReturnsAsync(new TicketStatus { Name = "In Progress", CompanyId = companyId });

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
            ticketStatusId: statusId);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId,
            originalStatusId: statusId);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync(entity);

        context.StatusRepositoryMock
            .Setup(x => x.GetAsync(request.TicketStatusId))
            .ReturnsAsync(new TicketStatus { Name = "In Progress", CompanyId = companyId });

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
    /// Verifies the error path when the company does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ShouldReturnInvalidCompanyError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY");
        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when the ticket is not found in the repository (returns null).
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketNotFound_ShouldReturnTicketNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.TicketRepositoryMock
            .Setup(x => x.GetAsync(request.Id))
            .ReturnsAsync((Ticket?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// Verifies the guard when the ticket status does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketStatusNotFound_ShouldReturnInvalidStatusError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync((TicketStatus?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_STATUS");
    }

    /// <summary>
    /// Verifies the guard when the ticket complexity does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketComplexityNotFound_ShouldReturnInvalidComplexityError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync((TicketComplexity?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_COMPLEXITY");
    }

    /// <summary>
    /// Verifies the guard when the time unit does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeUnitNotFound_ShouldReturnInvalidTimeUnitError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync((TimeUnit?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TIME_UNIT");
    }

    /// <summary>
    /// Verifies the guard when the communication channel does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelNotFound_ShouldReturnInvalidChannelError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand(id: _fixture.Create<Guid>());
        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync((CommunicationChannel?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CHANNEL");
    }

    /// <summary>
    /// Verifies the guard when the optional customer does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonNotFound_ShouldReturnInvalidPersonError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();

        var request = _fixture.Build<UpdateTicketCommand>()
            .With(x => x.Id, _fixture.Create<Guid>())
            .With(x => x.Name, "Ticket")
            .With(x => x.Description, "Desc")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 1m)
            .With(x => x.EffortPoints, 3m)
            .With(x => x.TicketStatusId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.PersonId, customerId)
            .Without(x => x.ProjectId)
            .Without(x => x.AreaId)
            .Without(x => x.PrecedentTicketId)
            .Create();

        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        context.PersonRepositoryMock.Setup(x => x.GetAsync(customerId)).ReturnsAsync((Person?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CUSTOMER");
    }

    /// <summary>
    /// Verifies the guard when the optional project does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectNotFound_ShouldReturnInvalidProjectError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var projectId = _fixture.Create<Guid>();

        var request = _fixture.Build<UpdateTicketCommand>()
            .With(x => x.Id, _fixture.Create<Guid>())
            .With(x => x.Name, "Ticket")
            .With(x => x.Description, "Desc")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 1m)
            .With(x => x.EffortPoints, 3m)
            .With(x => x.TicketStatusId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.ProjectId, projectId)
            .Without(x => x.PersonId)
            .Without(x => x.AreaId)
            .Without(x => x.PrecedentTicketId)
            .Create();

        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        context.PersonRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Person?)null);
        context.ProjectRepositoryMock.Setup(x => x.GetAsync(projectId)).ReturnsAsync((Project?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_PROJECT");
    }

    /// <summary>
    /// Verifies the guard when the optional area does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaNotFound_ShouldReturnInvalidAreaError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var areaId = _fixture.Create<Guid>();

        var request = _fixture.Build<UpdateTicketCommand>()
            .With(x => x.Id, _fixture.Create<Guid>())
            .With(x => x.Name, "Ticket")
            .With(x => x.Description, "Desc")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 1m)
            .With(x => x.EffortPoints, 3m)
            .With(x => x.TicketStatusId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.AreaId, areaId)
            .Without(x => x.PersonId)
            .Without(x => x.ProjectId)
            .Without(x => x.PrecedentTicketId)
            .Create();

        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        context.AreaRepositoryMock.Setup(x => x.GetAsync(areaId)).ReturnsAsync((Area?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_AREA");
    }

    /// <summary>
    /// Verifies the guard when a non-self precedent ticket does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPrecedentTicketNotFound_ShouldReturnInvalidPrecedentTicketError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var precedentId = _fixture.Create<Guid>();

        var request = CreateValidCommand(id: ticketId, precedentTicketId: precedentId);
        var entity = CreateExistingTicket(companyId, currentUserId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(ticketId)).ReturnsAsync(entity);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(precedentId)).ReturnsAsync((Ticket?)null);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_PRECEDENT_TICKET");
    }

    /// <summary>
    /// Verifies that only a StatusChange log is added when only the status changes.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyStatusChanges_ShouldAddOnlyStatusChangeLog()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var originalStatusId = _fixture.Create<Guid>();
        var newStatusId = _fixture.Create<Guid>();

        var request = CreateValidCommand(
            id: _fixture.Create<Guid>(),
            ticketStatusId: newStatusId);

        var entity = CreateExistingTicket(
            companyId: companyId,
            createdByUserId: currentUserId,
            originalStatusId: originalStatusId);

        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(newStatusId)).ReturnsAsync(new TicketStatus { Name = "Closed", CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId)).ReturnsAsync(new ApplicationUser { FirstName = "Ana", LastName = "Torres" });
        context.MapperMock.Setup(x => x.ApplyUpdate(request, entity)).Callback(() => ApplyRequestToTicket(request, entity));

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.TicketLogs.Should().ContainSingle(x => x.LogType == LogType.StatusChange);
        entity.TicketLogs.Should().NotContain(x => x.LogType == LogType.Reassignment);
    }

    /// <summary>
    /// SPEC 37 — guards against <c>UpdateTicket</c> moving a ticket to a status flagged
    /// <c>IsFinal = true</c>. The handler must reject with
    /// <c>USE_FINISH_TICKET_FOR_FINAL_STATUS</c> and never call
    /// <c>ITicketMapper.ApplyUpdate</c>, because closing a ticket must go through
    /// <c>FinishTicket</c> (the only path that produces the
    /// <c>LogType.Finalization</c> log the SLA calculator uses).
    /// </summary>
    [Fact]
    public async Task Handle_WhenTargetStatusIsFinal_ShouldReturnUseFinishTicketError_AndSkipApplyUpdate()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var originalStatusId = Guid.NewGuid();
        var newStatusId = Guid.NewGuid();
        var request = CreateValidCommand(id: _fixture.Create<Guid>(), ticketStatusId: newStatusId);

        var entity = CreateExistingTicket(companyId: companyId, createdByUserId: currentUserId, originalStatusId: originalStatusId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(newStatusId))
            .ReturnsAsync(new TicketStatus { Name = "Closed", IsFinal = true, CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USE_FINISH_TICKET_FOR_FINAL_STATUS");
        context.MapperMock.Verify(x => x.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// SPEC 37 — when the target status is non-final but the configured transition
    /// rules forbid moving from the existing status to the requested one, the
    /// handler rejects with <c>TICKET_STATUS_TRANSITION_NOT_ALLOWED</c> and persists
    /// nothing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTransitionNotAllowedByGuard_ShouldReturnTransitionNotAllowed_AndPersistNothing()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var originalStatusId = Guid.NewGuid();
        var newStatusId = Guid.NewGuid();
        var request = CreateValidCommand(id: _fixture.Create<Guid>(), ticketStatusId: newStatusId);

        var entity = CreateExistingTicket(companyId: companyId, createdByUserId: currentUserId, originalStatusId: originalStatusId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(newStatusId))
            .ReturnsAsync(new TicketStatus { Name = "InProgress", IsFinal = false, CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        // A rule for the source status that does NOT include the requested destination.
        context.TicketStatusTransitionRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                new TicketStatusTransition { CompanyId = companyId, FromStatusId = originalStatusId, ToStatusId = Guid.NewGuid() }
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_STATUS_TRANSITION_NOT_ALLOWED");
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Ticket>()), Times.Never);
    }

    /// <summary>
    /// SPEC 37 — the guard must not interfere when no transition rules are
    /// configured for the source status (the default state for every tenant at
    /// deploy time). Behavior matches the previous UpdateTicket handler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoTransitionRulesConfigured_ShouldSucceedAsBefore()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var originalStatusId = Guid.NewGuid();
        var newStatusId = Guid.NewGuid();
        var request = CreateValidCommand(id: _fixture.Create<Guid>(), ticketStatusId: newStatusId);

        var entity = CreateExistingTicket(companyId: companyId, createdByUserId: currentUserId, originalStatusId: originalStatusId);
        var context = CreateContext(companyId, currentUserId);

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        context.TicketRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(entity);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(newStatusId))
            .ReturnsAsync(new TicketStatus { Name = "InProgress", IsFinal = false, CompanyId = companyId });
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        context.TicketRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId)).ReturnsAsync(new ApplicationUser { FirstName = "Ana", LastName = "Torres" });
        context.MapperMock.Setup(x => x.ApplyUpdate(request, entity)).Callback(() => ApplyRequestToTicket(request, entity));

        var handler = context.CreateHandler();
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Creates a valid and controlled command for reuse across multiple tests.
    /// The method keeps the setup explicit so each test can focus on the branch it wants to validate.
    /// </summary>
    private UpdateTicketCommand CreateValidCommand(
        Guid id,
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
            .With(x => x.PersonId, (Guid?)null)
            .With(x => x.ProjectId, (Guid?)null)
            .With(x => x.AreaId, (Guid?)null)
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
        Guid? originalStatusId = null)
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
        entity.PersonId = request.PersonId;
        entity.ProjectId = request.ProjectId;
        entity.AreaId = request.AreaId;
        entity.ChannelId = request.ChannelId;
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
            SetupRepository(UnitOfWorkMock, PersonRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserCompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketStatusTransitionRepositoryMock);
            // Default: no transition rules configured — guard should allow any transition.
            TicketStatusTransitionRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketStatusTransition>());
            SetupRepository(UnitOfWorkMock, TicketLogRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketCompanyDefaultRepositoryMock);
            // Default: no ticket logs / no inactivity default for the tenant.
            TicketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());
            TicketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
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
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = CreateRepositoryMock<Person>();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = CreateRepositoryMock<Project>();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = CreateRepositoryMock<Area>();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = CreateRepositoryMock<ApplicationUser>();
        public Mock<IGenericRepository<UserCompany>> UserCompanyRepositoryMock { get; } = CreateRepositoryMock<UserCompany>();
        public Mock<IGenericRepository<TicketStatusTransition>> TicketStatusTransitionRepositoryMock { get; } = CreateRepositoryMock<TicketStatusTransition>();
        public Mock<IGenericRepository<TicketLog>> TicketLogRepositoryMock { get; } = CreateRepositoryMock<TicketLog>();
        public Mock<IGenericRepository<TicketCompanyDefault>> TicketCompanyDefaultRepositoryMock { get; } = CreateRepositoryMock<TicketCompanyDefault>();

        public UpdateTicketCommandHandler CreateHandler()
        {
            return new UpdateTicketCommandHandler(
                UnitOfWorkMock.Object,
                MapperMock.Object,
                CurrentUserServiceMock.Object,
                new TicketDtoAssembler(UnitOfWorkMock.Object),
                new TicketStatusTransitionGuard(UnitOfWorkMock.Object));
        }
    }
}
