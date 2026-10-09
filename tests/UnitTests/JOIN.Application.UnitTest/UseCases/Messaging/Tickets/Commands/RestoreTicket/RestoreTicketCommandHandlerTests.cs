using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.RestoreTicket;

/// <summary>
/// Unit tests for <see cref="RestoreTicketCommandHandler"/> (SPEC 41, Etapa 4): parents, cascade of attachments
/// and the <see cref="LogType.Restoration"/> log entry.
/// </summary>
public sealed class RestoreTicketCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly DateTime CascadeDay = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var ticket = context.SetupDeletedTicket();

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(ticket.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        ticket.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequired()
    {
        var context = new TestContext(userId: "not-a-guid");

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("USER_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenTicketBelongsToAnotherCompany_ShouldReturnNotFound()
    {
        var context = new TestContext();
        var ticket = context.SetupDeletedTicket(companyId: Guid.NewGuid());

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(ticket.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("complexity")]
    [InlineData("timeUnit")]
    [InlineData("channel")]
    [InlineData("person")]
    [InlineData("project")]
    [InlineData("area")]
    [InlineData("precedent")]
    public async Task Handle_WhenAParentIsDeleted_ShouldReturnParentDeleted(string parent)
    {
        var context = new TestContext();
        var ticket = context.SetupDeletedTicket(withOptionalParents: true);
        context.DeleteParent(parent);

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(ticket.Id), CancellationToken.None);

        response.Message.Should().Be("PARENT_DELETED");
        ticket.IsDeleted.Should().BeTrue();
        ticket.TicketLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestoreTicketAttachmentsOfTheCascadeAndLogTheRestoration()
    {
        var context = new TestContext();
        var ticket = context.SetupDeletedTicket();
        var sameCascade = new TicketDocument { CompanyId = CompanyId, TicketId = ticket.Id, OriginalName = "a.pdf" };
        sameCascade.MarkAsDeleted(CascadeDay);
        var deletedBefore = new TicketDocument { CompanyId = CompanyId, TicketId = ticket.Id, OriginalName = "b.pdf" };
        deletedBefore.MarkAsDeleted(CascadeDay.AddDays(-5));
        context.UnitOfWorkMock.SetupRepositoryRows<TicketDocument>([sameCascade, deletedBefore]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);

        var response = await context.CreateHandler().Handle(new RestoreTicketCommand(ticket.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ticket.IsDeleted.Should().BeFalse();
        sameCascade.IsDeleted.Should().BeFalse();
        deletedBefore.IsDeleted.Should().BeTrue("attachments deleted one by one before the cascade stay deleted");
        var log = ticket.TicketLogs.Should().ContainSingle().Subject;
        log.LogType.Should().Be(LogType.Restoration);
        log.UserRegisterLogId.Should().Be(ActorId);
        log.IsOnlyForCreatedAndAssigned.Should().BeFalse();
        context.TicketRepositoryMock.Verify(x => x.UpdateAsync(ticket), Times.Once);
    }

    private sealed class TestContext
    {
        private readonly Dictionary<string, BaseAuditableEntity> _parents = [];

        public TestContext(Guid? companyId = null, bool isSuperAdmin = true, string? userId = null)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId ?? ActorId.ToString());
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.SetupRepositoryRows<TicketDocument>([]);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepositoryMock { get; } = new();

        public Ticket SetupDeletedTicket(Guid? companyId = null, bool withOptionalParents = false)
        {
            var precedent = Parent<Ticket>("precedent", new Ticket { CompanyId = CompanyId, Name = "Precedent", Description = "-" });
            var ticket = new Ticket
            {
                CompanyId = companyId ?? CompanyId,
                Name = "Ticket",
                Description = "Deleted ticket",
                CreatedByUserId = Guid.NewGuid(),
                TicketStatusId = Parent<TicketStatus>("status", new TicketStatus { CompanyId = CompanyId, Name = "Open" }).Id,
                TicketComplexityId = Parent<TicketComplexity>("complexity", new TicketComplexity { CompanyId = CompanyId, Name = "Low" }).Id,
                TimeUnitId = Parent<TimeUnit>("timeUnit", new TimeUnit { CompanyId = CompanyId, Name = "Hours", Code = 1 }).Id,
                ChannelId = Parent<CommunicationChannel>("channel", new CommunicationChannel { Name = "Web" }).Id
            };
            if (withOptionalParents)
            {
                ticket.PersonId = Parent<Person>("person", new Person { CompanyId = CompanyId }).Id;
                ticket.ProjectId = Parent<Project>("project", new Project { CompanyId = CompanyId, Name = "P" }).Id;
                ticket.AreaId = Parent<Area>("area", new Area { CompanyId = CompanyId, Name = "A" }).Id;
                ticket.PrecedentTicketId = precedent.Id;
            }

            ticket.MarkAsDeleted(CascadeDay);
            TicketRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(ticket.Id)).ReturnsAsync(ticket);
            TicketRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(precedent.Id)).ReturnsAsync(precedent);
            return ticket;
        }

        public void DeleteParent(string key) => _parents[key].MarkAsDeleted();

        public RestoreTicketCommandHandler CreateHandler()
            => new(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object,
                new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object),
                new TicketCascadeCoordinator(UnitOfWorkMock.Object));

        private T Parent<T>(string key, T parent)
            where T : BaseAuditableEntity
        {
            _parents[key] = parent;
            if (typeof(T) != typeof(Ticket))
            {
                var repositoryMock = new Mock<IGenericRepository<T>>();
                repositoryMock.Setup(x => x.GetIncludingDeletedAsync(parent.Id)).ReturnsAsync(parent);
                UnitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(repositoryMock.Object);
            }

            return parent;
        }
    }
}
