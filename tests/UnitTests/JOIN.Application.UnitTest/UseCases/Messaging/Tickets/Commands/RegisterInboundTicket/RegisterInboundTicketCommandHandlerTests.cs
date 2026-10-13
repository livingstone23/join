using System.Linq.Expressions;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

/// <summary>
/// SPEC 47 F9 — <see cref="RegisterInboundTicketCommandHandler"/>: author and defaults guards, idempotency (also
/// under a lost race), requester resolution, built ticket and best-effort attachments.
/// </summary>
public sealed class RegisterInboundTicketCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenInboundChannelIsNotActive_ShouldReturnNotFoundWithoutInsert()
    {
        var context = new InboundContext();
        context.InboundChannels.Clear();

        var response = await context.HandleAsync(context.Command());

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.InboundChannelNotFound);
        context.Tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenCompanyHasNoSuperAdminTicket_ShouldReturnNoTicketAdminWithoutInsert()
    {
        var context = new InboundContext();
        context.TicketUsers.Single().IsSuperAdminTicket = false;

        var response = await context.HandleAsync(context.Command());

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.NoTicketAdmin);
        context.Tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenExternalMessageIdAlreadyRegistered_ShouldReturnExistingTicketWithoutInsert()
    {
        var context = new InboundContext();
        var existing = context.ExistingTicket("SM-RETRY");

        var response = await context.HandleAsync(context.Command(externalMessageId: "SM-RETRY"));

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be(TicketInboundErrorCodes.Duplicate);
        response.Data!.Id.Should().Be(existing.Id);
        context.TicketRepository.Verify(x => x.InsertAsync(It.IsAny<Ticket>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenTicketCompanyDefaultIsMissing_ShouldReturnDefaultsNotConfigured()
    {
        var context = new InboundContext();
        context.Defaults.Clear();

        var response = await context.HandleAsync(context.Command());

        response.Message.Should().Be(TicketInboundErrorCodes.DefaultsMissing);
        context.Tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenComplexityDefaultIsMissing_ShouldReturnDefaultsNotConfigured()
    {
        var context = new InboundContext();
        context.Defaults.Single().TicketComplexityDefaultId = null;

        var response = await context.HandleAsync(context.Command());

        response.Message.Should().Be(TicketInboundErrorCodes.DefaultsMissing);
        context.Tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenConfiguredInitialStatusIsFinal_ShouldReturnDefaultsNotConfigured()
    {
        var context = new InboundContext();
        context.Statuses.Single().IsFinal = true;

        var response = await context.HandleAsync(context.Command());

        response.Message.Should().Be(TicketInboundErrorCodes.DefaultsMissing);
        response.Errors.Should().NotBeNullOrEmpty();
        context.Tickets.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_HappyPath_ShouldCreateUnassignedVisibleTicketAuthoredByTheTicketAdmin()
    {
        var context = new InboundContext();

        var response = await context.HandleAsync(context.Command(externalMessageId: "SM-NEW"));

        response.IsSuccess.Should().BeTrue();
        response.Data!.Code.Should().StartWith("TICK-");
        var ticket = context.Tickets.Single();
        ticket.CompanyId.Should().Be(context.CompanyId);
        ticket.ChannelId.Should().Be(context.ChannelId);
        ticket.ExternalMessageId.Should().Be("SM-NEW");
        ticket.CreatedByUserId.Should().Be(context.AdminUserId);
        ticket.AssignedToUserId.Should().BeNull();
        ticket.IsVisibleToExternals.Should().BeTrue();
        ticket.TicketStatusId.Should().Be(context.Statuses.Single().Id);
        ticket.TicketComplexityId.Should().Be(context.Defaults.Single().TicketComplexityDefaultId!.Value);
        ticket.TimeUnitId.Should().Be(context.Defaults.Single().TimeUnitDefaultId!.Value);
        ticket.AreaId.Should().Be(context.Defaults.Single().AreaDefaultId);
        var creation = ticket.TicketLogs.Should().ContainSingle().Subject;
        creation.LogType.Should().Be(LogType.Creation);
        creation.Summary.Should().Contain("WhatsApp").And.Contain("+50588887777");
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenSenderMatchesAPerson_ShouldLinkThePersonWithoutSenderPrefix()
    {
        var context = new InboundContext();
        var personId = Guid.NewGuid();
        context.SetPersonMatch(personId);

        await context.HandleAsync(context.Command(body: "Necesito ayuda"));

        var ticket = context.Tickets.Single();
        ticket.PersonId.Should().Be(personId);
        ticket.Description.Should().Be("Necesito ayuda");
    }

    [Fact]
    public async Task Handle_WhenSenderMatchesNobody_ShouldLeaveSenderInDescription()
    {
        var context = new InboundContext();

        await context.HandleAsync(context.Command(body: "Necesito ayuda", displayName: "Ana"));

        var ticket = context.Tickets.Single();
        ticket.PersonId.Should().BeNull();
        ticket.Description.Should().Be("Remitente: Ana (+50588887777)\n\nNecesito ayuda");
    }

    [Fact]
    public async Task Handle_WhenSubjectIsEmpty_ShouldBuildNameFromBodyOnOneLineTruncatedTo150()
    {
        var context = new InboundContext();
        var body = "Primera línea\n" + new string('x', 300);

        await context.HandleAsync(context.Command(subject: string.Empty, body: body));

        var name = context.Tickets.Single().Name;
        name.Should().HaveLength(RegisterInboundTicketCommandHandler.NameMaxLength);
        name.Should().StartWith("Primera línea x");
    }

    [Fact]
    public async Task Handle_WhenSubjectIsPresent_ShouldUseItAsName()
    {
        var context = new InboundContext();

        await context.HandleAsync(context.Command(subject: "Factura duplicada", body: "Detalle"));

        context.Tickets.Single().Name.Should().Be("Factura duplicada");
    }

    [Fact]
    public async Task Handle_WhenSubjectAndBodyAreEmpty_ShouldNameTheTicketAfterTheSender()
    {
        var context = new InboundContext();

        await context.HandleAsync(context.Command(subject: string.Empty, body: string.Empty));

        context.Tickets.Single().Name.Should().Be("Mensaje de +50588887777");
    }

    [Fact]
    public async Task Handle_WhenBodyIsLong_ShouldTruncateDescriptionTo2000()
    {
        var context = new InboundContext();

        await context.HandleAsync(context.Command(body: new string('y', 5000)));

        context.Tickets.Single().Description.Should().HaveLength(RegisterInboundTicketCommandHandler.DescriptionMaxLength);
    }

    [Fact]
    public async Task Handle_WhenAttachmentPassesValidation_ShouldStoreItAnchoredToTheCreationLog()
    {
        var context = new InboundContext();
        context.WithSettings();

        await context.HandleAsync(context.Command(attachments: [Attachment("factura.pdf", 100)]));

        var ticket = context.Tickets.Single();
        var document = context.Documents.Should().ContainSingle().Subject;
        document.TicketId.Should().Be(ticket.Id);
        document.TicketLogsId.Should().Be(ticket.TicketLogs.Single(l => l.LogType == LogType.Creation).Id);
        document.DocumentType.Should().Be(DocumentType.Pdf);
        document.Path.Should().StartWith($"{context.CompanyId}/{ticket.Id}/");
        ticket.TicketLogs.Should().NotContain(l => l.LogType == LogType.InternalNote);
    }

    [Fact]
    public async Task Handle_WhenAttachmentIsTooLarge_ShouldSkipItAndAddAnInternalNote()
    {
        var context = new InboundContext();
        context.WithSettings(maxFileSizeBytes: 50);

        var response = await context.HandleAsync(context.Command(attachments: [Attachment("grande.pdf", 100)]));

        response.IsSuccess.Should().BeTrue();
        context.Documents.Should().BeEmpty();
        var note = context.Tickets.Single().TicketLogs.Single(l => l.LogType == LogType.InternalNote);
        note.IsOnlyForCreatedAndAssigned.Should().BeTrue();
        note.Summary.Should().Contain("grande.pdf");
    }

    [Fact]
    public async Task Handle_WhenAttachmentTypeIsNotAllowedOrUnknown_ShouldSkipIt()
    {
        var context = new InboundContext();
        context.WithSettings(allowed: DocumentType.Pdf);

        await context.HandleAsync(context.Command(attachments: [Attachment("foto.png", 10), Attachment("audio.ogg", 10)]));

        context.Documents.Should().BeEmpty();
        var note = context.Tickets.Single().TicketLogs.Single(l => l.LogType == LogType.InternalNote).Summary;
        note.Should().Contain("foto.png").And.Contain("audio.ogg");
    }

    [Fact]
    public async Task Handle_WhenFilesPerTicketLimitIsReached_ShouldSkipTheRest()
    {
        var context = new InboundContext();
        context.WithSettings(maxFilesPerTicket: 1);

        await context.HandleAsync(context.Command(attachments: [Attachment("a.pdf", 10), Attachment("b.pdf", 10)]));

        context.Documents.Should().ContainSingle().Which.OriginalName.Should().Be("a.pdf");
        context.Tickets.Single().TicketLogs.Single(l => l.LogType == LogType.InternalNote).Summary.Should().Contain("b.pdf");
    }

    [Fact]
    public async Task Handle_WhenDailyQuotaIsReached_ShouldSkipTheAttachment()
    {
        var context = new InboundContext();
        context.WithSettings(maxFilesPerDay: 1);
        context.Documents.Add(new TicketDocument { CompanyId = context.CompanyId, Created = DateTime.UtcNow, OriginalName = "previo.pdf" });

        await context.HandleAsync(context.Command(attachments: [Attachment("hoy.pdf", 10)]));

        context.Documents.Should().ContainSingle().Which.OriginalName.Should().Be("previo.pdf");
    }

    [Fact]
    public async Task Handle_WhenCompanyHasNoAttachmentSettings_ShouldSkipEveryAttachment()
    {
        var context = new InboundContext();

        var response = await context.HandleAsync(context.Command(attachments: [Attachment("a.pdf", 10)]));

        response.IsSuccess.Should().BeTrue();
        context.Documents.Should().BeEmpty();
        context.Tickets.Single().TicketLogs.Should().Contain(l => l.LogType == LogType.InternalNote);
    }

    [Fact]
    public async Task Handle_WhenStorageFails_ShouldSkipTheAttachmentAndStillCreateTheTicket()
    {
        var context = new InboundContext();
        context.WithSettings();
        context.FileStorage
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));

        var response = await context.HandleAsync(context.Command(attachments: [Attachment("a.pdf", 10)]));

        response.IsSuccess.Should().BeTrue();
        context.Documents.Should().BeEmpty();
        context.Tickets.Single().TicketLogs.Single(l => l.LogType == LogType.InternalNote).Summary.Should().Contain("almacenamiento");
    }

    [Fact]
    public async Task Handle_WhenControllerReportsSkippedDownloads_ShouldRecordThemInTheInternalNote()
    {
        var context = new InboundContext();

        await context.HandleAsync(context.Command(skipped: ["whatsapp-SM1-0.jpg: Twilio respondió 404"]));

        context.Tickets.Single().TicketLogs.Single(l => l.LogType == LogType.InternalNote)
            .Summary.Should().Contain("Twilio respondió 404");
        context.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_WhenAConcurrentRetryWinsTheExternalMessageIdIndex_ShouldReturnTheWinner()
    {
        var context = new InboundContext { AddTicketsOnInsert = false };
        Ticket? winner = null;
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => winner = context.ExistingTicket("SM-RACE"))
            .ThrowsAsync(new InvalidOperationException("23505"));
        context.ErrorClassifier
            .Setup(x => x.IsUniqueViolation(It.IsAny<Exception>(), TicketInboundErrorCodes.ExternalMessageIdIndex))
            .Returns(true);

        var response = await context.HandleAsync(context.Command(externalMessageId: "SM-RACE"));

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be(TicketInboundErrorCodes.Duplicate);
        response.Data!.Id.Should().Be(winner!.Id);
    }

    [Fact]
    public async Task Handle_WhenUniqueViolationHasNoWinner_ShouldRethrow()
    {
        var context = new InboundContext { AddTicketsOnInsert = false };
        context.UnitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("23505"));
        context.ErrorClassifier
            .Setup(x => x.IsUniqueViolation(It.IsAny<Exception>(), It.IsAny<string>()))
            .Returns(true);

        var act = () => context.HandleAsync(context.Command());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static InboundAttachment Attachment(string fileName, long length) =>
        new(new MemoryStream(new byte[length]), fileName, "application/octet-stream", length);

    /// <summary> In-memory tables behind a mocked <see cref="IUnitOfWork"/>, seeded with a fully configured company. </summary>
    private sealed class InboundContext
    {
        private readonly FakeSqlConnectionFactory _sql = new();

        public InboundContext()
        {
            InboundChannels.Add(new TicketInboundChannel { CompanyId = CompanyId, ChannelId = ChannelId, WebhookToken = "token" });
            var channel = new CommunicationChannel { Name = "WhatsApp", Code = CommunicationChannelCodes.WhatsApp };
            EntityIdSetter.SetId(channel, ChannelId);
            Channels.Add(channel);

            var company = new Company { Name = "JOIN", TaxId = "J0310000000000" };
            EntityIdSetter.SetId(company, CompanyId);
            Companies.Add(company);

            TicketUsers.Add(new TicketUserCompany { CompanyId = CompanyId, UserId = AdminUserId, IsSuperAdminTicket = true });

            var status = new TicketStatus { CompanyId = CompanyId, Name = "Abierto", IsActive = true };
            Statuses.Add(status);
            Defaults.Add(new TicketCompanyDefault
            {
                CompanyId = CompanyId,
                TicketStatusDefaultId = status.Id,
                TicketComplexityDefaultId = Guid.NewGuid(),
                TimeUnitDefaultId = Guid.NewGuid(),
                AreaDefaultId = Guid.NewGuid()
            });

            Table(InboundChannels);
            Table(Channels);
            Table(Companies);
            Table(TicketUsers);
            Table(Statuses);
            Table(Defaults);
            Table(Settings);
            Table(Documents);
            Table(new List<TicketLog>());
            Table(new List<TicketComplexity>());
            Table(new List<TimeUnit>());
            Table(new List<Person>());
            Table(new List<Project>());
            Table(new List<Area>());
            TicketRepository = Table(Tickets, () => AddTicketsOnInsert);

            var users = new Mock<IGenericRepository<ApplicationUser>>();
            UnitOfWork.Setup(x => x.GetRepository<ApplicationUser>()).Returns(users.Object);
            UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

            FileStorage
                .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream content, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, content.Length));

            FileStorage.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);

            FileStorageResolver
                .Setup(x => x.Current)
                .Returns(FileStorage.Object);
            FileStorageResolver
                .Setup(x => x.Resolve(It.IsAny<StorageProviderKind>()))
                .Returns(FileStorage.Object);

            _sql.SetResults(FakeResultSet.Empty("Id", "FullName", "HasPending"));
        }

        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid ChannelId { get; } = Guid.NewGuid();
        public Guid AdminUserId { get; } = Guid.NewGuid();
        public bool AddTicketsOnInsert { get; init; } = true;

        public List<TicketInboundChannel> InboundChannels { get; } = [];
        public List<CommunicationChannel> Channels { get; } = [];
        public List<Company> Companies { get; } = [];
        public List<TicketUserCompany> TicketUsers { get; } = [];
        public List<TicketStatus> Statuses { get; } = [];
        public List<TicketCompanyDefault> Defaults { get; } = [];
        public List<TicketAttachmentSettingsEntity> Settings { get; } = [];
        public List<TicketDocument> Documents { get; } = [];
        public List<Ticket> Tickets { get; } = [];

        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IGenericRepository<Ticket>> TicketRepository { get; }
        public Mock<IFileStorageService> FileStorage { get; } = new();
        public Mock<IFileStorageResolver> FileStorageResolver { get; } = new();
        public Mock<IDatabaseErrorClassifier> ErrorClassifier { get; } = new();

        public void SetPersonMatch(Guid personId) =>
            _sql.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = personId,
                ["FullName"] = "Ana Pérez",
                ["HasPending"] = false
            }));

        public void WithSettings(
            DocumentType allowed = DocumentType.Pdf | DocumentType.Image,
            long maxFileSizeBytes = 1_000_000,
            int maxFilesPerTicket = 5,
            int? maxFilesPerDay = null) =>
            Settings.Add(new TicketAttachmentSettingsEntity
            {
                CompanyId = CompanyId,
                AllowedDocumentTypes = allowed,
                MaxFileSizeBytes = maxFileSizeBytes,
                MaxFilesPerTicket = maxFilesPerTicket,
                MaxFilesPerDay = maxFilesPerDay
            });

        public Ticket ExistingTicket(string externalMessageId)
        {
            var ticket = new Ticket
            {
                CompanyId = CompanyId,
                ChannelId = ChannelId,
                ExternalMessageId = externalMessageId,
                Name = "Existente",
                TicketStatusId = Statuses.Single().Id,
                CreatedByUserId = AdminUserId
            };
            ticket.SetStandardCode(2026, 10, 7);
            Tickets.Add(ticket);
            return ticket;
        }

        public RegisterInboundTicketCommand Command(
            string externalMessageId = "SM-1",
            string subject = "",
            string body = "Hola, mi pedido no llegó",
            string? displayName = null,
            IReadOnlyList<InboundAttachment>? attachments = null,
            IReadOnlyList<string>? skipped = null) =>
            new(CompanyId, ChannelId, "+50588887777", displayName, subject, body, externalMessageId, attachments ?? [], skipped);

        public Task<JOIN.Application.Common.Response<JOIN.Application.DTO.Messaging.TicketDto>> HandleAsync(RegisterInboundTicketCommand command) =>
            new RegisterInboundTicketCommandHandler(
                    UnitOfWork.Object,
                    FileStorageResolver.Object,
                    new TicketDtoAssembler(UnitOfWork.Object),
                    new TicketCodeGenerator(UnitOfWork.Object),
                    new TicketInitialStatusResolver(UnitOfWork.Object),
                    new IntakeRequesterResolver(_sql),
                    ErrorClassifier.Object,
                    NullLogger<RegisterInboundTicketCommandHandler>.Instance)
                .Handle(command, CancellationToken.None);

        private Mock<IGenericRepository<T>> Table<T>(List<T> rows, Func<bool>? addOnInsert = null)
            where T : BaseEntity
        {
            var repository = new Mock<IGenericRepository<T>>();
            repository.Setup(x => x.GetAllAsync()).ReturnsAsync(() => rows.ToList());
            repository
                .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<T, bool>>>()))
                .ReturnsAsync((Expression<Func<T, bool>> predicate) => rows.Where(predicate.Compile()).ToList());
            repository.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Guid id) => rows.FirstOrDefault(r => r.Id == id));
            repository
                .Setup(x => x.InsertAsync(It.IsAny<T>()))
                .Callback<T>(row =>
                {
                    if (addOnInsert?.Invoke() ?? true)
                    {
                        rows.Add(row);
                    }
                })
                .ReturnsAsync(true);
            UnitOfWork.Setup(x => x.GetRepository<T>()).Returns(repository.Object);
            return repository;
        }
    }
}
