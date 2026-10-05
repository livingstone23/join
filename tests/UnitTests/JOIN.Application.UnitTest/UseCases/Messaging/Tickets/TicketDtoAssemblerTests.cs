using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets;

/// <summary>
/// Unit tests for <see cref="TicketDtoAssembler"/>. The assembler consolidates the
/// <c>Ticket</c> entity → <see cref="TicketDto"/> projection that used to be
/// duplicated between <c>CreateTicketCommandHandler</c> and
/// <c>UpdateTicketCommandHandler</c> (SPEC 35 F3). It must produce the same shape
/// with the same values as the old inline projection, including a safe fallback
/// when optional relations (<c>Person</c>, <c>Project</c>, <c>Area</c>,
/// <c>PrecedentTicket</c>) are absent.
/// </summary>
public sealed class TicketDtoAssemblerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the fully-populated happy path: every FK catalog lookup resolves,
    /// every related entity name is projected onto the DTO, and the optional
    /// <c>PersonName</c> resolver falls back to the concatenated first/middle/last
    /// names when <c>CommercialName</c> is empty.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenAllRelatedEntitiesResolve_ShouldProjectAllDtoFields()
    {
        // Arrange
        var context = new TicketDtoAssemblerTestContext();
        var companyId = _fixture.Create<Guid>();
        var company = new Company { Name = "JOIN" };
        SetEntityId(company, companyId);
        var ticket = CreateTicket(companyId);
        ticket.PersonId = _fixture.Create<Guid>();
        ticket.ProjectId = _fixture.Create<Guid>();
        ticket.AreaId = _fixture.Create<Guid>();
        ticket.PrecedentTicketId = _fixture.Create<Guid>();

        var customer = new Person
        {
            FirstName = "L",
            MiddleName = "M",
            LastName = "P",
            SecondLastName = "S",
            CommercialName = string.Empty
        };
        SetEntityId(customer, ticket.PersonId.Value);
        var project = new Project { Name = "CRM" };
        SetEntityId(project, ticket.ProjectId.Value);
        var area = new Area { Name = "Support" };
        SetEntityId(area, ticket.AreaId.Value);
        var precedent = new Ticket();
        precedent.SetStandardCode(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 7);
        SetEntityId(precedent, ticket.PrecedentTicketId.Value);

        var status = new TicketStatus { Name = "Open" };
        SetEntityId(status, ticket.TicketStatusId);
        var complexity = new TicketComplexity { Name = "High" };
        SetEntityId(complexity, ticket.TicketComplexityId);
        var timeUnit = new TimeUnit { Name = "Hours" };
        SetEntityId(timeUnit, ticket.TimeUnitId);
        var channel = new CommunicationChannel { Name = "Portal" };
        SetEntityId(channel, ticket.ChannelId);
        var creator = new ApplicationUser { FirstName = "Ana", LastName = "Torres" };
        var assignee = new ApplicationUser { FirstName = "Luis", LastName = "Gomez" };

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(ticket.TicketStatusId)).ReturnsAsync(status);
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(ticket.TicketComplexityId)).ReturnsAsync(complexity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(ticket.TimeUnitId)).ReturnsAsync(timeUnit);
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(ticket.ChannelId)).ReturnsAsync(channel);
        context.PersonRepositoryMock.Setup(x => x.GetAsync(ticket.PersonId.Value)).ReturnsAsync(customer);
        context.ProjectRepositoryMock.Setup(x => x.GetAsync(ticket.ProjectId.Value)).ReturnsAsync(project);
        context.AreaRepositoryMock.Setup(x => x.GetAsync(ticket.AreaId.Value)).ReturnsAsync(area);
        context.TicketRepositoryMock.Setup(x => x.GetAsync(ticket.PrecedentTicketId.Value)).ReturnsAsync(precedent);
        context.UserRepositoryMock.Setup(x => x.GetAsync(ticket.CreatedByUserId)).ReturnsAsync(creator);
        context.UserRepositoryMock.Setup(x => x.GetAsync(ticket.AssignedToUserId!.Value)).ReturnsAsync(assignee);

        // Act
        var dto = await context.CreateAssembler().BuildAsync(ticket, company, CancellationToken.None);

        // Assert
        dto.Id.Should().Be(ticket.Id);
        dto.CompanyId.Should().Be(companyId);
        dto.CompanyName.Should().Be("JOIN");
        dto.Code.Should().Be(ticket.Code);
        dto.Name.Should().Be(ticket.Name);
        dto.Description.Should().Be(ticket.Description);
        dto.EstimatedTime.Should().Be(ticket.EstimatedTime);
        dto.ConsumedTime.Should().Be(ticket.ConsumedTime);
        dto.EffortPoints.Should().Be(ticket.EffortPoints);
        dto.IsVisibleToExternals.Should().Be(ticket.IsVisibleToExternals);
        dto.TicketStatusId.Should().Be(ticket.TicketStatusId);
        dto.TicketStatusName.Should().Be("Open");
        dto.TicketComplexityId.Should().Be(ticket.TicketComplexityId);
        dto.TicketComplexityName.Should().Be("High");
        dto.TimeUnitId.Should().Be(ticket.TimeUnitId);
        dto.TimeUnitName.Should().Be("Hours");
        dto.PersonId.Should().Be(ticket.PersonId);
        dto.PersonName.Should().Be("L M P S");
        dto.ProjectId.Should().Be(ticket.ProjectId);
        dto.ProjectName.Should().Be("CRM");
        dto.AreaId.Should().Be(ticket.AreaId);
        dto.AreaName.Should().Be("Support");
        dto.ChannelId.Should().Be(ticket.ChannelId);
        dto.ChannelName.Should().Be("Portal");
        dto.CreatedByUserId.Should().Be(ticket.CreatedByUserId);
        dto.CreatedByUserName.Should().Be("Ana Torres");
        dto.AssignedToUserId.Should().Be(ticket.AssignedToUserId);
        dto.AssignedToUserName.Should().Be("Luis Gomez");
        dto.PrecedentTicketId.Should().Be(ticket.PrecedentTicketId);
        dto.PrecedentTicketCode.Should().Be(precedent.Code);
        dto.CreatedAt.Should().Be(ticket.Created);
        dto.Logs.Should().NotBeNull().And.BeEmpty(); // logs are NOT assembled here — GetTicketByIdQueryHandler owns them
    }

    /// <summary>
    /// Verifies that the optional relations <c>PersonId</c>, <c>ProjectId</c>,
    /// <c>AreaId</c>, and <c>PrecedentTicketId</c> being <c>null</c> does not
    /// break the projection — the assembler must not call the corresponding
    /// repositories when the FK is null, and the resulting DTO must keep the
    /// name fields null rather than throwing or returning placeholder strings.
    /// </summary>
    [Fact]
    public async Task BuildAsync_WhenOptionalRelationsAreNull_ShouldNotInvokeTheirRepositories()
    {
        // Arrange
        var context = new TicketDtoAssemblerTestContext();
        var companyId = _fixture.Create<Guid>();
        var company = new Company { Name = "JOIN" };
        SetEntityId(company, companyId);
        var ticket = CreateTicket(companyId, includeOptionalRelations: false);

        var status = new TicketStatus { Name = "Open" };
        SetEntityId(status, ticket.TicketStatusId);
        var complexity = new TicketComplexity { Name = "High" };
        SetEntityId(complexity, ticket.TicketComplexityId);
        var timeUnit = new TimeUnit { Name = "Hours" };
        SetEntityId(timeUnit, ticket.TimeUnitId);
        var channel = new CommunicationChannel { Name = "Portal" };
        SetEntityId(channel, ticket.ChannelId);
        var creator = new ApplicationUser { FirstName = "Ana", LastName = "Torres" };

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(ticket.TicketStatusId)).ReturnsAsync(status);
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(ticket.TicketComplexityId)).ReturnsAsync(complexity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(ticket.TimeUnitId)).ReturnsAsync(timeUnit);
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(ticket.ChannelId)).ReturnsAsync(channel);
        context.UserRepositoryMock.Setup(x => x.GetAsync(ticket.CreatedByUserId)).ReturnsAsync(creator);

        // Act
        var dto = await context.CreateAssembler().BuildAsync(ticket, company, CancellationToken.None);

        // Assert
        dto.PersonId.Should().BeNull();
        dto.PersonName.Should().BeNull();
        dto.ProjectId.Should().BeNull();
        dto.ProjectName.Should().BeNull();
        dto.AreaId.Should().BeNull();
        dto.AreaName.Should().BeNull();
        dto.PrecedentTicketId.Should().BeNull();
        dto.PrecedentTicketCode.Should().BeNull();
        dto.AssignedToUserId.Should().BeNull();
        dto.AssignedToUserName.Should().BeNull();

        context.PersonRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
        context.ProjectRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
        context.AreaRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
        context.TicketRepositoryMock.Verify(x => x.GetAsync(It.IsAny<Guid>()), Times.Never);
    }

    /// <summary>
    /// SPEC 37 — the assembler must compute the four SLA/inactivity fields identically
    /// to the read queries. A ticket created two hours ago with no activity log, a
    /// complexity pointing at <c>Día</c> (Code 24) with <c>ResolutionTimeUnits = 3</c>,
    /// and no inactivity threshold must surface <c>SlaDueAt = Created + 72h</c> and
    /// <c>IsSlaBreached = false</c>.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ShouldComputeSlaAndInactivityFields()
    {
        // Arrange
        var context = new TicketDtoAssemblerTestContext();
        var companyId = _fixture.Create<Guid>();
        var company = new Company { Name = "JOIN" };
        SetEntityId(company, companyId);
        var ticket = CreateTicket(companyId, includeOptionalRelations: false);
        ticket.Created = DateTime.UtcNow.AddHours(-1);

        var status = new TicketStatus { Name = "Open", IsFinal = false };
        SetEntityId(status, ticket.TicketStatusId);
        var complexityTimeUnitId = Guid.NewGuid();
        var complexity = new TicketComplexity
        {
            Name = "High",
            ResolutionTimeUnits = 3,
            TimeUnitId = complexityTimeUnitId
        };
        SetEntityId(complexity, ticket.TicketComplexityId);
        var complexityTimeUnit = new TimeUnit { Name = "Día", Code = 24 };
        SetEntityId(complexityTimeUnit, complexityTimeUnitId);
        var timeUnit = new TimeUnit { Name = "Hora", Code = 1 };
        SetEntityId(timeUnit, ticket.TimeUnitId);
        var channel = new CommunicationChannel { Name = "Portal" };
        SetEntityId(channel, ticket.ChannelId);
        var creator = new ApplicationUser { FirstName = "Ana", LastName = "Torres" };

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(ticket.TicketStatusId)).ReturnsAsync(status);
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(ticket.TicketComplexityId)).ReturnsAsync(complexity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(ticket.TimeUnitId)).ReturnsAsync(timeUnit);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(complexityTimeUnitId)).ReturnsAsync(complexityTimeUnit);
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(ticket.ChannelId)).ReturnsAsync(channel);
        context.UserRepositoryMock.Setup(x => x.GetAsync(ticket.CreatedByUserId)).ReturnsAsync(creator);
        context.TicketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());
        context.TicketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());

        // Act
        var dto = await context.CreateAssembler().BuildAsync(ticket, company, CancellationToken.None);

        // Assert
        dto.SlaDueAt.Should().Be(ticket.Created.AddHours(72));
        dto.IsSlaBreached.Should().BeFalse();
        dto.IsInactive.Should().BeFalse();
    }

    /// <summary>
    /// Discriminating test: the assembler must use the <c>TimeUnit</c> of the
    /// complexity, NOT the <c>TimeUnit</c> of the ticket. Both units are loaded
    /// via <c>IGenericRepository&lt;TimeUnit&gt;.GetAsync</c> on distinct ids —
    /// swapping them silently produces an SLA 24× too short.
    /// </summary>
    [Fact]
    public async Task BuildAsync_ShouldUseComplexityTimeUnit_NotTicketTimeUnit()
    {
        // Arrange
        var context = new TicketDtoAssemblerTestContext();
        var companyId = _fixture.Create<Guid>();
        var company = new Company { Name = "JOIN" };
        SetEntityId(company, companyId);
        var ticket = CreateTicket(companyId, includeOptionalRelations: false);
        ticket.Created = DateTime.UtcNow.AddHours(-1);

        var status = new TicketStatus { Name = "Open", IsFinal = false };
        SetEntityId(status, ticket.TicketStatusId);
        var complexityTimeUnitId = Guid.NewGuid();
        var complexity = new TicketComplexity
        {
            Name = "High",
            ResolutionTimeUnits = 3,
            TimeUnitId = complexityTimeUnitId
        };
        SetEntityId(complexity, ticket.TicketComplexityId);
        var complexityTimeUnit = new TimeUnit { Name = "Día", Code = 24 };
        SetEntityId(complexityTimeUnit, complexityTimeUnitId);
        var ticketTimeUnit = new TimeUnit { Name = "Hora", Code = 1 };
        SetEntityId(ticketTimeUnit, ticket.TimeUnitId);
        var channel = new CommunicationChannel { Name = "Portal" };
        SetEntityId(channel, ticket.ChannelId);
        var creator = new ApplicationUser { FirstName = "Ana", LastName = "Torres" };

        context.CompanyRepositoryMock.Setup(x => x.GetAsync(companyId)).ReturnsAsync(company);
        context.StatusRepositoryMock.Setup(x => x.GetAsync(ticket.TicketStatusId)).ReturnsAsync(status);
        context.ComplexityRepositoryMock.Setup(x => x.GetAsync(ticket.TicketComplexityId)).ReturnsAsync(complexity);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(ticket.TimeUnitId)).ReturnsAsync(ticketTimeUnit);
        context.TimeUnitRepositoryMock.Setup(x => x.GetAsync(complexityTimeUnitId)).ReturnsAsync(complexityTimeUnit);
        context.ChannelRepositoryMock.Setup(x => x.GetAsync(ticket.ChannelId)).ReturnsAsync(channel);
        context.UserRepositoryMock.Setup(x => x.GetAsync(ticket.CreatedByUserId)).ReturnsAsync(creator);
        context.TicketLogRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());
        context.TicketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketCompanyDefault>());

        // Act
        var dto = await context.CreateAssembler().BuildAsync(ticket, company, CancellationToken.None);

        // Assert
        dto.TimeUnitName.Should().Be("Hora"); // ticket unit (effort tracking)
        dto.SlaDueAt.Should().Be(ticket.Created.AddHours(72)); // complexity unit (SLA)
    }

    private Ticket CreateTicket(Guid companyId, bool includeOptionalRelations = true) => new()
    {
        CompanyId = companyId,
        Name = "Sample ticket",
        Description = "Description",
        EstimatedTime = 4m,
        ConsumedTime = 1m,
        EffortPoints = 2m,
        IsVisibleToExternals = true,
        TicketStatusId = Guid.NewGuid(),
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        CreatedByUserId = Guid.NewGuid(),
        AssignedToUserId = includeOptionalRelations ? _fixture.Create<Guid>() : null,
        PersonId = includeOptionalRelations ? _fixture.Create<Guid>() : null,
        ProjectId = includeOptionalRelations ? _fixture.Create<Guid>() : null,
        AreaId = includeOptionalRelations ? _fixture.Create<Guid>() : null,
        PrecedentTicketId = includeOptionalRelations ? _fixture.Create<Guid>() : null,
        Created = DateTime.UtcNow.AddDays(-1)
    };

    /// <summary>
    /// Sets the <see cref="BaseEntity.Id"/> of an entity via reflection — the setter
    /// is private to keep ids domain-owned, but tests need to fabricate rows whose
    /// ids match the FKs on the ticket under test.
    /// </summary>
    private static void SetEntityId(BaseEntity entity, Guid id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);

    /// <summary>
    /// Wires every repository the assembler reads from. Tests set up only the
    /// mocks they exercise; unmocked mocks return null, which mirrors production
    /// behavior (the assembler tolerates a missing catalog row by emitting an
    /// empty name string).
    /// </summary>
    private sealed class TicketDtoAssemblerTestContext
    {
        public TicketDtoAssemblerTestContext()
        {
            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketRepositoryMock);
            SetupRepository(UnitOfWorkMock, UserRepositoryMock);
            SetupRepository(UnitOfWorkMock, StatusRepositoryMock);
            SetupRepository(UnitOfWorkMock, ComplexityRepositoryMock);
            SetupRepository(UnitOfWorkMock, TimeUnitRepositoryMock);
            SetupRepository(UnitOfWorkMock, ChannelRepositoryMock);
            SetupRepository(UnitOfWorkMock, PersonRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProjectRepositoryMock);
            SetupRepository(UnitOfWorkMock, AreaRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketLogRepositoryMock);
            SetupRepository(UnitOfWorkMock, TicketCompanyDefaultRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketStatus>> StatusRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketComplexity>> ComplexityRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TimeUnit>> TimeUnitRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Project>> ProjectRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Area>> AreaRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketLog>> TicketLogRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketCompanyDefault>> TicketCompanyDefaultRepositoryMock { get; } = new();

        public TicketDtoAssembler CreateAssembler() => new(UnitOfWorkMock.Object);

        private static void SetupRepository<TEntity>(
            Mock<IUnitOfWork> unitOfWorkMock,
            Mock<IGenericRepository<TEntity>> repositoryMock)
            where TEntity : class
        {
            unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
        }
    }
}