using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
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
        var status = new TicketStatus { Name = "Abierto", CompanyId = companyId };
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
        var customerRepositoryMock = CreateRepositoryMock<Person>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(status);
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
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

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
    public async Task Handle_WhenPersonDoesNotExist_ShouldReturnInvalidPersonError()
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
        var customerRepositoryMock = CreateRepositoryMock<Person>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Abierto", CompanyId = companyId });
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "Alta" });
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Horas" });
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "WhatsApp" });
        customerRepositoryMock.Setup(x => x.GetAsync(customerId)).ReturnsAsync((Person?)null);

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
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CUSTOMER");
        response.Data.Should().BeNull();

        ticketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Ticket>()), Times.Never);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        mapperMock.Verify(x => x.ToEntity(It.IsAny<CreateTicketCommand>()), Times.Never);
    }

    /// <summary>
    /// Verifies that a code already taken by a soft-deleted ticket is skipped: the next
    /// sequence is computed from every ticket of the company, deleted ones included.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCodeWasUsedByDeletedTicket_ShouldSkipToNextSequence()
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
        duplicatedTicket.MarkAsDeleted();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        var customerRepositoryMock = CreateRepositoryMock<Person>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Abierto", CompanyId = companyId });
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "Alta" });
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Horas" });
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync(new CommunicationChannel { Name = "Portal" });
        ticketRepositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Ticket, bool>>>()))
            .ReturnsAsync(new[] { duplicatedTicket });
        ticketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
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
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.Code.Should().Be($"TICK-{now:yyyyMM}-0002", "a soft-deleted ticket's code must never be reissued");
        ticketRepositoryMock.Verify(x => x.InsertAsync(entity), Times.Once);
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
        var customerRepositoryMock = CreateRepositoryMock<Person>();
        var projectRepositoryMock = CreateRepositoryMock<Project>();
        var areaRepositoryMock = CreateRepositoryMock<Area>();
        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());

        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Abierto", CompanyId = companyId });
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
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");

        ticketRepositoryMock.Verify(x => x.InsertAsync(entity), Times.Once);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the CompanyId claim is missing from the token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        currentUserServiceMock.SetupGet(x => x.UserId).Returns(_fixture.Create<Guid>().ToString());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        unitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the early exit when the UserId claim is not a valid GUID.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequiredError()
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(_fixture.Create<Guid>());
        currentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
        response.Errors.Should().Contain("The authenticated user identifier is required.");
        unitOfWorkMock.Verify(x => x.GetRepository<Ticket>(), Times.Never);
    }

    /// <summary>
    /// Verifies the branch when the tenant company does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyNotFound_ShouldReturnInvalidCompanyError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync((Company?)null);
        SetupRepository(unitOfWorkMock, companyRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY");
        response.Errors.Should().Contain("The provided company does not exist or is inactive.");
        mapperMock.Verify(x => x.ToEntity(It.IsAny<CreateTicketCommand>()), Times.Never);
    }

    /// <summary>
    /// Verifies the guard when the ticket status does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketStatusNotFound_ShouldReturnInvalidStatusError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync((TicketStatus?)null);
        SetupRepository(unitOfWorkMock, statusRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_STATUS");
        response.Errors.Should().Contain("The ticket status must be active, not final and belong to the current company.");
    }

    /// <summary>
    /// Verifies the guard when the ticket complexity does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketComplexityNotFound_ShouldReturnInvalidComplexityError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync((TicketComplexity?)null);
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_COMPLEXITY");
        response.Errors.Should().Contain("The provided ticket complexity does not exist or is inactive.");
    }

    /// <summary>
    /// Verifies the guard when the time unit does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTimeUnitNotFound_ShouldReturnInvalidTimeUnitError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync((TimeUnit?)null);
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TIME_UNIT");
        response.Errors.Should().Contain("The provided time unit does not exist or is inactive.");
    }

    /// <summary>
    /// Verifies the guard when the communication channel does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenChannelNotFound_ShouldReturnInvalidChannelError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        var statusRepositoryMock = CreateRepositoryMock<TicketStatus>();
        statusRepositoryMock.Setup(x => x.GetAsync(request.TicketStatusId!.Value)).ReturnsAsync(new TicketStatus { Name = "Open", CompanyId = companyId });
        SetupRepository(unitOfWorkMock, statusRepositoryMock);
        var complexityRepositoryMock = CreateRepositoryMock<TicketComplexity>();
        complexityRepositoryMock.Setup(x => x.GetAsync(request.TicketComplexityId)).ReturnsAsync(new TicketComplexity { Name = "High" });
        SetupRepository(unitOfWorkMock, complexityRepositoryMock);
        var timeUnitRepositoryMock = CreateRepositoryMock<TimeUnit>();
        timeUnitRepositoryMock.Setup(x => x.GetAsync(request.TimeUnitId)).ReturnsAsync(new TimeUnit { Name = "Hours" });
        SetupRepository(unitOfWorkMock, timeUnitRepositoryMock);
        var channelRepositoryMock = CreateRepositoryMock<CommunicationChannel>();
        channelRepositoryMock.Setup(x => x.GetAsync(request.ChannelId)).ReturnsAsync((CommunicationChannel?)null);
        SetupRepository(unitOfWorkMock, channelRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CHANNEL");
        response.Errors.Should().Contain("The provided communication channel does not exist or is inactive.");
    }

    /// <summary>
    /// Verifies the guard when an optional project identifier is provided but does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProjectNotFound_ShouldReturnInvalidProjectError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var projectId = _fixture.Create<Guid>();
        var request = CreateValidCommand();
        request = _fixture.Build<CreateTicketCommand>()
            .With(x => x.Name, request.Name)
            .With(x => x.Description, request.Description)
            .With(x => x.EstimatedTime, request.EstimatedTime)
            .With(x => x.ConsumedTime, request.ConsumedTime)
            .With(x => x.EffortPoints, request.EffortPoints)
            .With(x => x.IsVisibleToExternals, request.IsVisibleToExternals)
            .With(x => x.TicketStatusId, request.TicketStatusId)
            .With(x => x.TicketComplexityId, request.TicketComplexityId)
            .With(x => x.TimeUnitId, request.TimeUnitId)
            .With(x => x.ChannelId, request.ChannelId)
            .With(x => x.ProjectId, projectId)
            .Without(x => x.PersonId)
            .Without(x => x.AreaId)
            .Without(x => x.AssignedToUserId)
            .Without(x => x.PrecedentTicketId)
            .Create();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        var customerRepo = CreateRepositoryMock<Person>();
        SetupRepository(unitOfWorkMock, customerRepo);
        var projectRepo = CreateRepositoryMock<Project>();
        projectRepo.Setup(x => x.GetAsync(projectId)).ReturnsAsync((Project?)null);
        SetupRepository(unitOfWorkMock, projectRepo);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Ticket>());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_PROJECT");
        response.Errors.Should().Contain("The provided project does not exist for the current company.");
    }

    /// <summary>
    /// Verifies the guard when an optional area identifier is provided but does not exist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAreaNotFound_ShouldReturnInvalidAreaError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var areaId = _fixture.Create<Guid>();

        var request = _fixture.Build<CreateTicketCommand>()
            .With(x => x.Name, "Ticket")
            .With(x => x.Description, "Description")
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
            .Without(x => x.AssignedToUserId)
            .Without(x => x.PrecedentTicketId)
            .Create();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        var areaRepo = CreateRepositoryMock<Area>();
        areaRepo.Setup(x => x.GetAsync(areaId)).ReturnsAsync((Area?)null);
        SetupRepository(unitOfWorkMock, areaRepo);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Ticket>());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_AREA");
        response.Errors.Should().Contain("The provided area does not exist for the current company.");
    }

    /// <summary>
    /// Verifies the guard when the assigned user does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignedUserNotFound_ShouldReturnInvalidAssignedUserError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var assignedUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand(assignedToUserId: assignedUserId);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Area>());
        var userRepo = CreateRepositoryMock<ApplicationUser>();
        userRepo.Setup(x => x.GetAsync(assignedUserId)).ReturnsAsync((ApplicationUser?)null);
        SetupRepository(unitOfWorkMock, userRepo);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Ticket>());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER");
        response.Errors.Should().Contain("The assigned user does not exist or is inactive.");
    }

    /// <summary>
    /// Verifies the guard when the assigned user has a tenant link but lacks the
    /// <c>CanResolveTicket</c> capability in <c>TicketUserCompany</c> (SPEC 35 F5).
    /// The handler must reject the initial assignment with
    /// <c>INVALID_ASSIGNED_USER_NOT_RESOLVER</c> even when the user exists and is a
    /// member of the tenant — only roster resolvers can be assigned at creation time.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignedUserIsNotResolver_ShouldReturnInvalidAssignedUserNotResolverError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var assignedUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand(assignedToUserId: assignedUserId);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Area>());
        var userRepo = CreateRepositoryMock<ApplicationUser>();
        userRepo.Setup(x => x.GetAsync(assignedUserId)).ReturnsAsync(new ApplicationUser { FirstName = "Test", LastName = "User" });
        SetupRepository(unitOfWorkMock, userRepo);
        // The user is a tenant member (UserCompany row exists) but does NOT have
        // CanResolveTicket = 1 — the resolver reads this roster and rejects.
        var userCompanyRepo = CreateRepositoryMock<UserCompany>();
        userCompanyRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { new UserCompany { UserId = assignedUserId, CompanyId = companyId, GcRecord = 0 } });
        SetupRepository(unitOfWorkMock, userCompanyRepo);
        // SPEC 38: assigned user IS linked to the current tenant — IsActiveMemberAsync returns true.
        var userCompanyNamedRepo = new Mock<JOIN.Application.Interface.Persistence.Security.IUserCompanyRepository>();
        userCompanyNamedRepo
            .Setup(x => x.IsActiveMemberAsync(assignedUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        unitOfWorkMock.Setup(x => x.UserCompanies).Returns(userCompanyNamedRepo.Object);
        var ticketUserCompanyRepo = CreateRepositoryMock<TicketUserCompany>();
        // Empty roster ⇒ assigned user has no flags, so CanResolveTicket = false.
        ticketUserCompanyRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketUserCompany>());
        SetupRepository(unitOfWorkMock, ticketUserCompanyRepo);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Ticket>());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER_NOT_RESOLVER");
        response.Errors.Should().Contain("The assigned user does not have ticket-resolution capability for the current company.");
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the guard when the assigned user is not linked to the current company tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAssignedUserNotLinkedToCompany_ShouldReturnInvalidAssignedUserTenantError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var assignedUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand(assignedToUserId: assignedUserId);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Area>());
        var userRepo = CreateRepositoryMock<ApplicationUser>();
        userRepo.Setup(x => x.GetAsync(assignedUserId)).ReturnsAsync(new ApplicationUser { FirstName = "Test", LastName = "User" });
        SetupRepository(unitOfWorkMock, userRepo);
        var userCompanyRepo = CreateRepositoryMock<UserCompany>();
        userCompanyRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<UserCompany>());
        SetupRepository(unitOfWorkMock, userCompanyRepo);
        // SPEC 38: assigned user is NOT linked to the current tenant — IsActiveMemberAsync returns false.
        var userCompanyNamedRepo = new Mock<JOIN.Application.Interface.Persistence.Security.IUserCompanyRepository>();
        userCompanyNamedRepo
            .Setup(x => x.IsActiveMemberAsync(assignedUserId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        unitOfWorkMock.Setup(x => x.UserCompanies).Returns(userCompanyNamedRepo.Object);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Ticket>());

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ASSIGNED_USER_TENANT");
        response.Errors.Should().Contain("The assigned user is not linked to the current company.");
    }

    /// <summary>
    /// Verifies the guard when the optional precedent ticket does not exist in the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPrecedentTicketNotFound_ShouldReturnInvalidPrecedentTicketError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var precedentId = _fixture.Create<Guid>();

        var request = _fixture.Build<CreateTicketCommand>()
            .With(x => x.Name, "Ticket")
            .With(x => x.Description, "Description")
            .With(x => x.EstimatedTime, 8m)
            .With(x => x.ConsumedTime, 1m)
            .With(x => x.EffortPoints, 3m)
            .With(x => x.TicketStatusId, _fixture.Create<Guid>())
            .With(x => x.TicketComplexityId, _fixture.Create<Guid>())
            .With(x => x.TimeUnitId, _fixture.Create<Guid>())
            .With(x => x.ChannelId, _fixture.Create<Guid>())
            .With(x => x.PrecedentTicketId, precedentId)
            .Without(x => x.PersonId)
            .Without(x => x.ProjectId)
            .Without(x => x.AreaId)
            .Without(x => x.AssignedToUserId)
            .Create();

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        SetupRepository(unitOfWorkMock, SetupCompany(companyId));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Area>());
        var ticketRepo = CreateRepositoryMock<Ticket>();
        ticketRepo.Setup(x => x.GetAsync(precedentId)).ReturnsAsync((Ticket?)null);
        SetupRepository(unitOfWorkMock, ticketRepo);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_PRECEDENT_TICKET");
        response.Errors.Should().Contain("The precedent ticket does not exist for the current company.");
    }

    /// <summary>
    /// Verifies that a personalized code is generated when the company has a UsePersonalizedCode default enabled.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonalizedCodeEnabled_ShouldUsePersonalizedCodeFormat()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();

        var request = CreateValidCommand();
        var entity = CreateMappedTicket(request);
        var createdByUser = new ApplicationUser { FirstName = "Luis", LastName = "Cano" };

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepositoryMock = CreateRepositoryMock<Company>();
        companyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        SetupRepository(unitOfWorkMock, companyRepositoryMock);

        SetupRepository(unitOfWorkMock, SetupStatus<TicketStatus>(request.TicketStatusId!.Value, new TicketStatus { Name = "Open", CompanyId = companyId }));
        SetupRepository(unitOfWorkMock, SetupStatus<TicketComplexity>(request.TicketComplexityId, new TicketComplexity { Name = "High" }));
        SetupRepository(unitOfWorkMock, SetupStatus<TimeUnit>(request.TimeUnitId, new TimeUnit { Name = "Hours" }));
        SetupRepository(unitOfWorkMock, SetupStatus<CommunicationChannel>(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Person>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Project>());
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<Area>());

        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        ticketRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Ticket>());
        ticketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        SetupRepository(unitOfWorkMock, ticketRepositoryMock);

        var ticketCompanyDefaultRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());
        ticketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new TicketCompanyDefault
            {
                CompanyId = companyId,
                GcRecord = 0,
                UsePersonalizedCode = true,
                StartCode = "SUPPORT",
                CodeSequenceLength = 5
            }
        });
        SetupRepository(unitOfWorkMock, ticketCompanyDefaultRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);

        var userRepositoryMock = CreateRepositoryMock<ApplicationUser>();
        userRepositoryMock.Setup(x => x.GetAsync(currentUserId)).ReturnsAsync(createdByUser);
        SetupRepository(unitOfWorkMock, userRepositoryMock);

        mapperMock.Setup(x => x.ToEntity(request)).Returns(entity);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Code.Should().StartWith("SUPPORT-");
        response.Data.Code.Should().MatchRegex(@"^SUPPORT-\d{5}$");
    }

    // ──────────────────────────────────────────────
    //  Private arrange helpers (new tests only)
    // ──────────────────────────────────────────────

    /// <summary>
    /// SPEC 42 — when the request omits the status, the ticket starts in the company's
    /// configured initial status (<c>TicketCompanyDefaults.TicketStatusDefaultId</c>).
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusIsOmitted_ShouldUseCompanyDefaultStatus()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var defaultStatus = new TicketStatus { Name = "Abierto", CompanyId = companyId, IsInitial = true };
        var request = CreateValidCommand() with { TicketStatusId = null };
        var entity = CreateMappedTicket(request);

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        var ticketRepositoryMock = CreateRepositoryMock<Ticket>();
        ticketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<Ticket>())).ReturnsAsync(true);
        var defaultsRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        defaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[]
        {
            new TicketCompanyDefault { CompanyId = companyId, TicketStatusDefaultId = defaultStatus.Id }
        });
        var ticketLogRepositoryMock = CreateRepositoryMock<TicketLog>();
        ticketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());

        SetupRepository(unitOfWorkMock, ticketRepositoryMock);
        SetupRepository(unitOfWorkMock, defaultsRepositoryMock);
        SetupRepository(unitOfWorkMock, ticketLogRepositoryMock);
        SetupRepository(unitOfWorkMock, SetupStatus(defaultStatus.Id, defaultStatus));
        SetupRepository(unitOfWorkMock, SetupStatus(request.TicketComplexityId, new TicketComplexity { Name = "Alta" }));
        SetupRepository(unitOfWorkMock, SetupStatus(request.TimeUnitId, new TimeUnit { Name = "Horas" }));
        SetupRepository(unitOfWorkMock, SetupStatus(request.ChannelId, new CommunicationChannel { Name = "Portal" }));
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<ApplicationUser>());
        mapperMock.Setup(x => x.ToEntity(request)).Returns(entity);
        unitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.TicketStatusId.Should().Be(defaultStatus.Id);
        response.Data!.TicketStatusId.Should().Be(defaultStatus.Id);
        ticketRepositoryMock.Verify(x => x.InsertAsync(entity), Times.Once);
    }

    /// <summary>
    /// SPEC 42 — a status sent by the client that is final is rejected before anything is mapped or saved.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestedStatusIsFinal_ShouldReturnInvalidStatusError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand();

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        SetupRepository(unitOfWorkMock, SetupStatus(request.TicketStatusId!.Value, new TicketStatus { Name = "Cerrado", CompanyId = companyId, IsFinal = true }));

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_STATUS");
        mapperMock.Verify(x => x.ToEntity(It.IsAny<CreateTicketCommand>()), Times.Never);
        unitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SPEC 42 — without a status in the request and without a configured initial status, creation fails.
    /// </summary>
    [Fact]
    public async Task Handle_WhenStatusIsOmittedAndNoDefaultConfigured_ShouldReturnDefaultNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var request = CreateValidCommand() with { TicketStatusId = null };

        var (unitOfWorkMock, mapperMock, currentUserServiceMock) = BuildBaseArrange(companyId, currentUserId);
        SetupRepository(unitOfWorkMock, CreateRepositoryMock<TicketStatus>());
        var defaultsRepositoryMock = CreateRepositoryMock<TicketCompanyDefault>();
        defaultsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());
        SetupRepository(unitOfWorkMock, defaultsRepositoryMock);

        var handler = new CreateTicketCommandHandler(unitOfWorkMock.Object, mapperMock.Object, currentUserServiceMock.Object, new TicketDtoAssembler(unitOfWorkMock.Object), new TicketUserCompanyCapabilityResolver(unitOfWorkMock.Object), new TicketCodeGenerator(unitOfWorkMock.Object), new TicketInitialStatusResolver(unitOfWorkMock.Object));

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_DEFAULT_STATUS_NOT_CONFIGURED");
        mapperMock.Verify(x => x.ToEntity(It.IsAny<CreateTicketCommand>()), Times.Never);
    }

    /// <summary>
    /// Builds the minimum authenticated base arrange (company exists, catalogues not yet set up).
    /// Returns all three mocks so callers can append specific repository setups.
    /// </summary>
    private (Mock<IUnitOfWork> unitOfWork, Mock<ITicketMapper> mapper, Mock<ICurrentUserService> currentUser)
        BuildBaseArrange(Guid companyId, Guid currentUserId)
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        var mapperMock = new Mock<ITicketMapper>();
        var currentUserServiceMock = CreateCurrentUserServiceMock(companyId, currentUserId);

        var companyRepo = CreateRepositoryMock<Company>();
        companyRepo.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        SetupRepository(unitOfWorkMock, companyRepo);

        return (unitOfWorkMock, mapperMock, currentUserServiceMock);
    }

    /// <summary>
    /// Creates a company repository mock that returns a valid company for the given tenant id.
    /// </summary>
    private Mock<IGenericRepository<Company>> SetupCompany(Guid companyId)
    {
        var repo = CreateRepositoryMock<Company>();
        repo.Setup(x => x.GetAsync(companyId)).ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });
        return repo;
    }

    /// <summary>
    /// Creates a generic repository mock that returns the given entity for the given id.
    /// Used for simple FK catalogue lookups.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> SetupStatus<TEntity>(Guid id, TEntity entity)
        where TEntity : class
    {
        var repo = new Mock<IGenericRepository<TEntity>>();
        repo.Setup(x => x.GetAsync(id)).ReturnsAsync(entity);
        return repo;
    }

    /// <summary>
    /// Creates a valid and controlled command to reuse across multiple tests.
    /// Simple and consistent values are assigned so that each scenario focuses
    /// only on the business rule it is intended to validate.
    /// </summary>
    private CreateTicketCommand CreateValidCommand(Guid? customerId = null, Guid? assignedToUserId = null)
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
            .With(x => x.PersonId, customerId)
            .Without(x => x.ProjectId)
            .Without(x => x.AreaId)
            .With(x => x.AssignedToUserId, assignedToUserId)
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
            TicketStatusId = request.TicketStatusId ?? Guid.Empty,
            TicketComplexityId = request.TicketComplexityId,
            TimeUnitId = request.TimeUnitId,
            PersonId = request.PersonId,
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
