using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.CreateTicket;

/// <summary>
/// Contains the unit tests for the ticket creation flow.
/// It validates the happy path and the highest-risk business errors,
/// because those branches have the greatest impact on coverage and functional stability.
/// </summary>
public sealed class CreateTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the full happy path.
    /// This test ensures that, when all dependencies return valid data,
    /// the handler generates the ticket code, records the creation log, inserts the entity,
    /// and returns a successful DTO with the expected information.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateTicketAndReturnSuccessResponse()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand();
        var entity = CreateMappedTicket(request);
        var company = new Company { Name = _fixture.Create<string>(), TaxId = _fixture.Create<string>() };
        var status = new TicketStatus { Name = "Abierto" };
        var complexity = new TicketComplexity { Name = "Alta" };
        var timeUnit = new TimeUnit { Name = "Horas" };
        var channel = new CommunicationChannel { Name = "Portal Web" };
        var createdByUser = new ApplicationUser { FirstName = "Ana", LastName = "Torres" };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        var customerRepositoryMock = CreateRepositoryMock<Customer>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(status);
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(complexity);
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(timeUnit);
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(channel);
        ticketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        ticketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        userRepositoryMock.Setup(x => x.GetAsync(currentUserId)).ReturnsAsync(createdByUser);
        ticketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
        mapperMock.Setup(x => x.ToEntity(request)).Returns(entity);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        SetupRepository(unitOfWorkMock, companyRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketRepositoryMock);
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);
        SetupRepository(unitOfWorkMock, channelRepositoryMock);
        SetupRepository(unitOfWorkMock, customerRepositoryMock);
        SetupRepository(unitOfWorkMock, projectRepositoryMock);
        SetupRepository(unitOfWorkMock, areaRepositoryMock);
        SetupRepository(unitOfWorkMock, userRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketCompanyDefaultRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket created successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(companyId);
        response.Data.CompanyName.Should().Be(company.Name);
        response.Data.Name.Should().Be(request.Name);
        response.Data.ChannelName.Should().Be(channel.Name);
        response.Data.CreatedByUserId.Should().Be(currentUserId);
        response.Data.CreatedByUserName.Should().Be("Ana Torres");
        response.Data.Code.Should().MatchRegex(@"^TICK-\d{6}-\d{4}$");
        entity.Code.Should().Be(response.Data.Code);
        entity.CompanyId.Should().Be(companyId);
        entity.CreatedByUserId.Should().Be(currentUserId);
        entity.TicketLogs.Should().ContainSingle();

        ticketRepositoryMock.Verify(x => x.InsertAsync(entity), Times.Once);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the requested error case for a missing customer.
    /// This test protects a critical early-exit branch to prevent a ticket
    /// from being persisted with invalid references inside the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomerDoesNotExist_ShouldReturnInvalidCustomerError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();

        var request = CreateValidCommand(customerId: customerId);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        var customerRepositoryMock = CreateRepositoryMock<Customer>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Abierto" });
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "Alta" });
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Horas" });
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "WhatsApp" });
        customerRepositoryMock.Setup(x => x.GetAsync(customerId)).ReturnsAsync((Customer?)null);

        SetupRepository(unitOfWorkMock, companyRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketRepositoryMock);
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);
        SetupRepository(unitOfWorkMock, channelRepositoryMock);
        SetupRepository(unitOfWorkMock, customerRepositoryMock);
        SetupRepository(unitOfWorkMock, projectRepositoryMock);
        SetupRepository(unitOfWorkMock, areaRepositoryMock);
        SetupRepository(unitOfWorkMock, userRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketCompanyDefaultRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CUSTOMER");
        response.Data.Should().BeNull();

        ticketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Ticket>()), Times.Never);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        mapperMock.Verify(x => x.ToEntity(It.IsAny<CreateTicketCommand>()), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler rejects a duplicated code.
    /// This test covers the branch where the ticket has already been mapped and coded,
    /// but the business logic stops persistence to preserve uniqueness.
    /// </summary>
    [Fact]
    public async Task Handle_WhenGeneratedCodeAlreadyExists_ShouldReturnTicketCodeInUseError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand();
        var entity = CreateMappedTicket(request);
        var now = DateTime.UtcNow;
        var duplicatedTicket = CreateMappedTicket(request);
        duplicatedTicket.CompanyId = companyId;
        duplicatedTicket.Created = now.AddMonths(-1);
        duplicatedTicket.SetStandardCode(now.Year, now.Month, 1);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        var customerRepositoryMock = CreateRepositoryMock<Customer>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Abierto" });
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "Alta" });
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Horas" });
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        ticketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { duplicatedTicket });
        ticketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
        mapperMock.Setup(x => x.ToEntity(request)).Returns(entity);

        SetupRepository(unitOfWorkMock, companyRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketRepositoryMock);
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);
        SetupRepository(unitOfWorkMock, channelRepositoryMock);
        SetupRepository(unitOfWorkMock, customerRepositoryMock);
        SetupRepository(unitOfWorkMock, projectRepositoryMock);
        SetupRepository(unitOfWorkMock, areaRepositoryMock);
        SetupRepository(unitOfWorkMock, userRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketCompanyDefaultRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_CODE_IN_USE");

        ticketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Ticket>()), Times.Never);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the error path when the database does not confirm changes.
    /// This test protects the post-insert branch to ensure the handler
    /// does not report success when real persistence fails.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnCreateFailedError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand();
        var entity = CreateMappedTicket(request);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        var customerRepositoryMock = CreateRepositoryMock<Customer>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId)).ReturnsAsync(new TicketStatus { Name = "Abierto" });
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "Alta" });
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Horas" });
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        ticketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        ticketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        ticketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
        mapperMock.Setup(x => x.ToEntity(request)).Returns(entity);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        SetupRepository(unitOfWorkMock, companyRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketRepositoryMock);
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);
        SetupRepository(unitOfWorkMock, channelRepositoryMock);
        SetupRepository(unitOfWorkMock, customerRepositoryMock);
        SetupRepository(unitOfWorkMock, projectRepositoryMock);
        SetupRepository(unitOfWorkMock, areaRepositoryMock);
        SetupRepository(unitOfWorkMock, userRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketCompanyDefaultRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");

        ticketRepositoryMock.Verify(x => x.InsertAsync(entity), Times.Once);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid and controlled command to reuse across multiple tests.
    /// Simple and consistent values are assigned so that each scenario focuses
    /// only on the business rule it is intended to validate.
    /// </summary>
    private CreateTicketCommand CreateValidCommand(Guid? customerId = null)
    {
        return _fixture.Build<CreateTicketCommand>()
            .With(x => x.Name, "Ticket de prueba")
            .With(x => x.Description, "Descripción de prueba")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 2m)
            .With(x => x.EffortPoints, 3m)
            .With(x => x.IsVisibleToExternals, true)
            .With(x => x.TicketStatusId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.CustomerId, customerId)
            .Without(x => x.ProjectId)
            .Without(x => x.AreaId)
            .Without(x => x.AssignedToUserId)
            .Without(x => x.PrecedentTicketId)
            .Create();
    }

    /// <summary>
    /// Builds the entity returned by the mocked mapper.
    /// It is used to emulate the real mapping behavior without depending on the generator,
    /// keeping the test focused on the handler.
    /// </summary>
    private static Ticket CreateMappedTicket(CreateTicketCommand request)
    {
        return new Ticket
        {
            Name = request.Name,
            Description = request.Description,
            EstimatedTime = request.EstimatedTime,
            ConsumedTime = request.ConsumedTime,
            EffortPoints = request.EffortPoints,
            IsVisibleToExternals = request.IsVisibleToExternals,
            TicketStatusId = request.TicketStatusId,
            TicketComplexityId = request.TicketComplexityId,
            TimeUnitId = request.TimeUnitId,
            CustomerId = request.CustomerId,
            ProjectId = request.ProjectId,
            AreaId = request.AreaId,
            ChannelId = request.ChannelId,
            AssignedToUserId = request.AssignedToUserId,
            PrecedentTicketId = request.PrecedentTicketId
        };
    }

    /// <summary>
    /// Creates a mock for the authenticated user context.
    /// This allows the tenant and the current user to be simulated, both of which are required
    /// for the handler to execute its multi-tenant logic.
    /// </summary>
    private static Mock<ICurrentUserService> CreateCurrentUserServiceMock(Guid companyId, Guid currentUserId)
    {
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        currentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
        currentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
        return currentUserServiceMock;
    }

    /// <summary>
    /// Creates a generic repository mock to reduce noise in the arrange section.
    /// This improves test readability and makes the scenario intent more visible.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>() where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers each repository in the mocked unit of work.
    /// This helper replicates the generic resolution used by the handler in production.
    /// </summary>
    private static void SetupRepository<TEntity>(Mock<IUnitOfWork> unitOfWorkMock, Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }
}
