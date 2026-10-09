using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RestoreTicketDocument;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.RestoreTicketDocument;

/// <summary>
/// Unit tests for <see cref="RestoreTicketDocumentCommandHandler"/> (SPEC 41, Etapa 4).
/// </summary>
public sealed class RestoreTicketDocumentCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.HandleAsync(Guid.NewGuid(), Guid.NewGuid());

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var document = context.SetupDeletedDocument();

        var response = await context.HandleAsync(context.Ticket.Id, document.Id);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenTicketIsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var document = context.SetupDeletedDocument();
        context.Ticket.MarkAsDeleted();

        var response = await context.HandleAsync(context.Ticket.Id, document.Id);

        response.Message.Should().Be("PARENT_DELETED");
        document.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDocumentBelongsToAnotherTicket_ShouldReturnNotFound()
    {
        var context = new TestContext();
        var document = context.SetupDeletedDocument();

        var response = await context.HandleAsync(Guid.NewGuid(), document.Id);

        response.Message.Should().Be("NOT_FOUND");
        document.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenTicketReachedMaxFiles_ShouldReturnMaxFilesPerTicketReached()
    {
        var context = new TestContext();
        var document = context.SetupDeletedDocument(activeSiblings: 2);
        context.SetupSettings(maxFilesPerTicket: 2);

        var response = await context.HandleAsync(context.Ticket.Id, document.Id);

        response.Message.Should().Be("MAX_FILES_PER_TICKET_REACHED");
        document.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenBelowMaxFiles_ShouldRestore()
    {
        var context = new TestContext();
        var document = context.SetupDeletedDocument(activeSiblings: 1);
        context.SetupSettings(maxFilesPerTicket: 2);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.HandleAsync(context.Ticket.Id, document.Id);

        response.IsSuccess.Should().BeTrue();
        document.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenTenantHasNoSettings_ShouldRestoreWithoutQuota()
    {
        var context = new TestContext();
        var document = context.SetupDeletedDocument(activeSiblings: 10);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.HandleAsync(context.Ticket.Id, document.Id);

        response.IsSuccess.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            var ticketRepositoryMock = new Mock<IGenericRepository<Ticket>>();
            ticketRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(Ticket.Id)).ReturnsAsync(Ticket);
            UnitOfWorkMock.Setup(x => x.GetRepository<Ticket>()).Returns(ticketRepositoryMock.Object);
            UnitOfWorkMock.SetupRepositoryRows<TicketAttachmentSettingsEntity>([]);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Ticket Ticket { get; } = new() { CompanyId = CompanyId, Name = "T", Description = "-" };

        public TicketDocument SetupDeletedDocument(int activeSiblings = 0)
        {
            var document = new TicketDocument { CompanyId = CompanyId, TicketId = Ticket.Id, OriginalName = "a.pdf" };
            document.MarkAsDeleted();
            var rows = new List<TicketDocument> { document };
            rows.AddRange(Enumerable.Range(0, activeSiblings)
                .Select(i => new TicketDocument { CompanyId = CompanyId, TicketId = Ticket.Id, OriginalName = $"{i}.pdf" }));
            var repositoryMock = UnitOfWorkMock.SetupRepositoryRows<TicketDocument>(rows);
            repositoryMock.Setup(x => x.GetIncludingDeletedAsync(document.Id)).ReturnsAsync(document);
            return document;
        }

        public void SetupSettings(int maxFilesPerTicket)
            => UnitOfWorkMock.SetupRepositoryRows<TicketAttachmentSettingsEntity>(
                [new TicketAttachmentSettingsEntity { CompanyId = CompanyId, MaxFilesPerTicket = maxFilesPerTicket, MaxFileSizeBytes = 1024 }]);

        public Task<Response<Guid>> HandleAsync(Guid ticketId, Guid id)
            => new RestoreTicketDocumentCommandHandler(
                    UnitOfWorkMock.Object,
                    CurrentUserServiceMock.Object,
                    new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object))
                .Handle(new RestoreTicketDocumentCommand(ticketId, id), CancellationToken.None);
    }
}
