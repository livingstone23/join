using System.IO;
using System.Text;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;
using TicketEntity = JOIN.Domain.Messaging.Ticket;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.RegisterInboundTicket;

/// <summary>
/// SPEC 47 F9 — unit tests for <see cref="RegisterInboundTicketCommandHandler"/>.
/// Covers all fourteen branches enumerated in the spec: tenant gate, channel
/// gate, dedup, super admin guard, defaults guard, requester resolution,
/// subject truncation, attribution/visibility, attachment best-effort and the
/// happy path.
/// </summary>
public sealed class RegisterInboundTicketCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Tenant gate: with no inbound channel for the supplied (CompanyId, ChannelId),
    /// the handler short-circuits with INVALID_TOKEN before touching anything else.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoInboundChannelForTenant_ShouldReturnInvalidTokenError()
    {
        var context = new RegisterInboundTicketTestContext();
        context.InboundChannelRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketInboundChannel>());

        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.InvalidToken);
        context.TicketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketEntity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// The roster of <c>TicketUserCompany.IsSuperAdminTicket</c> determines who
    /// the new ticket is attributed to. Without one, the handler rejects and
    /// the inbound message is not silently dropped.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoSuperAdminTicketUserConfigured_ShouldReturnNoTicketAdminConfiguredError()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketUserCompany>());

        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.NoTicketAdmin);
        context.TicketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketEntity>()), Times.Never);
    }

    /// <summary>
    /// Without a complete <c>TicketCompanyDefault</c> the handler cannot assign
    /// a status/complexity/time-unit. The inbound message is rejected without
    /// a ticket being persisted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketCompanyDefaultsMissing_ShouldReturnDefaultsNotConfiguredError()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        context.TicketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketCompanyDefault>());

        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(TicketInboundErrorCodes.DefaultsMissing);
        context.TicketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketEntity>()), Times.Never);
    }

    /// <summary>
    /// The repetition of an inbound <c>ExternalMessageId</c> (dedup hit) is the
    /// happy idempotency branch: the existing ticket is returned without a new
    /// <c>InsertAsync</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExistingTicketWithSameExternalMessageId_ShouldReturnExistingTicketWithoutInsert()
    {
        var context = new RegisterInboundTicketTestContext();
        var companyId = context.Defaults.CompanyId;
        var channelId = context.Defaults.ChannelId;
        var inboundChannel = context.SetupActiveInboundChannel();
        context.InboundChannelRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { inboundChannel });

        var request = context.CreateValidCommand();
        var existing = new TicketEntity
        {
            CompanyId = companyId,
            ChannelId = channelId,
            ExternalMessageId = request.ExternalMessageId,
            Name = "Existing",
            Description = "Existing",
            EstimatedTime = 0m,
            ConsumedTime = 0m,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = channelId,
            CreatedByUserId = Guid.NewGuid(),
            GcRecord = BaseAuditableEntity.ActiveGcRecord
        };
        EntityIdSetter.SetId(existing, Guid.NewGuid());
        context.TicketRepositoryMock.Setup(x => x.GetAllIncludingDeletedAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<TicketEntity, bool>>>()))
            .ReturnsAsync(new[] { existing });

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be(TicketInboundErrorCodes.Duplicate);
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(existing.Id);
        // The "existing ticket found" branch must NOT issue a second insert:
        // idempotency is the whole point of dedup.
        context.TicketRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketEntity>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// When the inbound sender matches an active <c>PersonContact</c>, the
    /// created ticket must carry the resolved <c>PersonId</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonContactMatchesSender_ShouldSetRequesterPersonId()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();
        var senderEmail = "remitente@example.com";
        var matchedPersonId = Guid.NewGuid();
        context.PersonContactRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                PersonContact.Create(
                    context.Defaults.CompanyId,
                    matchedPersonId,
                    ContactType.PrimaryEmail,
                    senderEmail)
            });

        var request = context.CreateValidCommand() with
        {
            SenderIdentifier = senderEmail,
            SenderDisplayName = "Remitente Test"
        };

        var response = await context.CreateHandler().Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket.Should().NotBeNull();
        capturedTicket!.PersonId.Should().Be(matchedPersonId);
    }

    /// <summary>
    /// When no <c>PersonContact</c> matches, the ticket is created with
    /// <c>PersonId = null</c> and the sender is preserved verbatim in the
    /// Description block — per SPEC 47, the agent binds the identity later.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoPersonContactMatch_ShouldLeavePersonIdNullAndPrefixDescriptionWithRemitente()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();
        context.PersonContactRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<PersonContact>());

        var response = await context.CreateHandler().Handle(context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket.Should().NotBeNull();
        capturedTicket!.PersonId.Should().BeNull();
        capturedTicket.Description.Should().StartWith("Remitente: ");
        capturedTicket.Description.Should().Contain(context.Defaults.SenderIdentifier);
    }

    /// <summary>
    /// A subject longer than 150 chars is truncated and saved as
    /// <c>Ticket.Name</c>. Same name length that
    /// <c>CreateTicketCommandValidator</c> enforces for the regular path.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSubjectExceeds150Chars_ShouldTruncateNameToAtMost150()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();

        var longSubject = new string('A', 250);
        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand() with { MessageSubject = longSubject },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket!.Name.Length.Should().BeLessThanOrEqualTo(150);
    }

    /// <summary>
    /// A ticket born of an external channel must be visible to its external
    /// requester by construction — guaranteed by SPEC 47 F4.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreated_ShouldSetIsVisibleToExternalsTrue()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();

        var response = await context.CreateHandler().Handle(context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket!.IsVisibleToExternals.Should().BeTrue();
    }

    /// <summary>
    /// The creator is always the resolved super admin — never null, never the
    /// anonymous webhook sender.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreated_ShouldSetCreatedByUserIdToResolvedSuperAdmin()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();

        var response = await context.CreateHandler().Handle(context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket!.CreatedByUserId.Should().Be(context.Defaults.SuperAdminUserId);
        capturedTicket.CreatedByUserId.Should().NotBe(Guid.Empty);
    }

    /// <summary>
    /// On creation, the assignee is null — distribution falls to
    /// <c>ReassignTicket</c> (SPEC 35), not to the inbound channel.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCreated_ShouldLeaveAssignedToUserIdNull()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        var capturedTicket = context.SetupTicketInsertCapture();

        var response = await context.CreateHandler().Handle(context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedTicket!.AssignedToUserId.Should().BeNull();
    }

    /// <summary>
    /// An attachment that passes every gate (size, extension, allowed type)
    /// is persisted as a <c>TicketDocument</c>.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttachmentPassesAllGates_ShouldInsertTicketDocument()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        context.SetupTicketInsertCapture();
        context.SetupAttachmentPersistence(scenario: AttachmentScenario.SavesAndPersists);

        var attachment = CreateAttachment("report.pdf", length: 512);
        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand() with { Attachments = new[] { attachment } },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.DocumentRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketDocument>()), Times.Once);
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(
            attachment.Content, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// An attachment that exceeds <c>MaxFileSizeBytes</c> is silently skipped
    /// without aborting the ticket creation. SPEC 47 calls this "best effort".
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttachmentExceedsMaxFileSize_ShouldSkipWithoutFailingTicketCreation()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        context.SetupTicketInsertCapture();
        context.SetupAttachmentPersistence(scenario: AttachmentScenario.SavesAndPersists);

        // settings max = 1024 bytes, attachment length = 5000
        var oversized = CreateAttachment("report.pdf", length: 5000);
        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand() with { Attachments = new[] { oversized } },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.DocumentRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketDocument>()), Times.Never);
    }

    /// <summary>
    /// A file with a disallowed extension is skipped in the same best-effort
    /// way (no exception, no TicketDocument).
    /// </summary>
    [Fact]
    public async Task Handle_WhenAttachmentExtensionIsDisallowed_ShouldSkipWithoutFailingTicketCreation()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        context.SetupTicketInsertCapture();
        context.SetupAttachmentPersistence(scenario: AttachmentScenario.SavesAndPersists);

        var banned = CreateAttachment("payload.exe", length: 512);
        var response = await context.CreateHandler().Handle(
            context.CreateValidCommand() with { Attachments = new[] { banned } },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.DocumentRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TicketDocument>()), Times.Never);
    }

    /// <summary>
    /// Happy path: a request with no attachments walks through every gate
    /// and lands a ticket plus a <c>Creation</c> log entry whose summary
    /// mentions the channel and the sender.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValidWithoutAttachments_ShouldCreateTicketAndLogCreationEvent()
    {
        var context = new RegisterInboundTicketTestContext();
        context.SetupActiveInboundChannel();
        context.SetupDedupReturnsNoMatch();
        context.SetupSuperAdmin();
        context.SetupTicketInsertCapture();

        var response = await context.CreateHandler().Handle(context.CreateValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket registered successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.CompanyId.Should().Be(context.Defaults.CompanyId);
        // The TicketLog added by the handler is observable via the captured entity.
        var capturedTicket = context.CapturedTicket;
        capturedTicket.Should().NotBeNull();
        capturedTicket!.TicketLogs.Should().ContainSingle();
        var log = capturedTicket.TicketLogs.Single();
        log.LogType.Should().Be(LogType.Creation);
        log.Summary.Should().Contain("WhatsApp");
    }

    private static InboundAttachment CreateAttachment(string fileName, long length) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes("x")), fileName, "application/pdf", length);

    /// <summary>
    /// Permutation of attachment settings used by the three attachment tests.
    /// </summary>
    private enum AttachmentScenario
    {
        /// <summary>Saves the bytes via <see cref="IFileStorageService"/> and inserts a <see cref="TicketDocument"/>.</summary>
        SavesAndPersists
    }

    /// <summary>
    /// Holds the reusable mocks shared across the spec branches.
    /// </summary>
    private sealed class RegisterInboundTicketTestContext
    {
        private static readonly InboundAttachment[] NoAttachments = Array.Empty<InboundAttachment>();

        public RegisterInboundTicketTestContext()
        {
            Defaults = new ScenarioDefaults();

            // Every CompanyId flow goes through this lookup at the very end (BuildResponseAsync).
            CompanyRepositoryMock.Setup(x => x.GetAsync(Defaults.CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM" });

            // Catalogue repos the assembler pulls from — providing empty stubs
            // is sufficient because they map to optional slots.
            DefaultCatalogRepos();

            // Settings, contacts and TicketAttachmentSettings default to empty.
            PersonContactRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(Array.Empty<PersonContact>());
            SettingsRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(Array.Empty<TicketAttachmentSettingsEntity>());

            InboundChannelRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(Array.Empty<TicketInboundChannel>());

            // New SaveChangesAsync calls during a typical run — two flushes
            // (1) ticket + log, (2) documents — must both succeed. We let the
            // unmocked call return 0 by default, which trips the persistence
            // branch; tests that want success set it explicitly.
            UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        /// <summary>
        /// Resolved baseline of company/ids/super admin shared by the tests.
        /// </summary>
        public ScenarioDefaults Defaults { get; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketInboundChannel>> InboundChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketEntity>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketUserCompany>> TicketUserCompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketCompanyDefault>> TicketCompanyDefaultRepositoryMock { get; } = new();
        public Mock<IGenericRepository<CommunicationChannel>> ChannelRepositoryMock { get; } = new();
        public Mock<IGenericRepository<PersonContact>> PersonContactRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketAttachmentSettingsEntity>> SettingsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketDocument>> DocumentRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        /// <summary>
        /// The single ticket entity inserted during the happy-path tests. Reading
        /// this gives tests access to the captured <see cref="TicketEntity"/> instance.
        /// </summary>
        public TicketEntity? CapturedTicket { get; private set; }

        public IRequestHandler<RegisterInboundTicketCommand, Response<TicketDto>> CreateHandler()
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketInboundChannel>()).Returns(InboundChannelRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketEntity>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketUserCompany>()).Returns(TicketUserCompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketCompanyDefault>()).Returns(TicketCompanyDefaultRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<CommunicationChannel>()).Returns(ChannelRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonContact>()).Returns(PersonContactRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketAttachmentSettingsEntity>()).Returns(SettingsRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketDocument>()).Returns(DocumentRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);

            var assembler = new TicketDtoAssembler(UnitOfWorkMock.Object);
            var codeGenerator = new TicketCodeGenerator(UnitOfWorkMock.Object);
            return new RegisterInboundTicketCommandHandler(
                UnitOfWorkMock.Object,
                FileStorageServiceMock.Object,
                assembler,
                codeGenerator,
                NullLogger<RegisterInboundTicketCommandHandler>.Instance);
        }

        /// <summary>
        /// Generates a valid <see cref="RegisterInboundTicketCommand"/> bound to
        /// the test's baseline company + channel.
        /// </summary>
        public RegisterInboundTicketCommand CreateValidCommand() => new(
            CompanyId: Defaults.CompanyId,
            ChannelId: Defaults.ChannelId,
            SenderIdentifier: Defaults.SenderIdentifier,
            SenderDisplayName: Defaults.SenderDisplayName,
            MessageSubject: Defaults.Subject,
            MessageBody: Defaults.Body,
            ExternalMessageId: Defaults.ExternalMessageId,
            Attachments: NoAttachments);

        /// <summary>
        /// Installs an active inbound channel for the current scenario.
        /// </summary>
        public TicketInboundChannel SetupActiveInboundChannel()
        {
            var channel = new TicketInboundChannel
            {
                CompanyId = Defaults.CompanyId,
                ChannelId = Defaults.ChannelId,
                WebhookToken = "token",
                GcRecord = BaseAuditableEntity.ActiveGcRecord
            };
            EntityIdSetter.SetId(channel, Guid.NewGuid());
            InboundChannelRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(new[] { channel });
            return channel;
        }

        /// <summary>
        /// Wires the dedup lookup to return no match.
        /// </summary>
        public void SetupDedupReturnsNoMatch()
        {
            TicketRepositoryMock.Setup(x => x.GetAllIncludingDeletedAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<TicketEntity, bool>>>()))
                .ReturnsAsync(Array.Empty<TicketEntity>());
        }

        /// <summary>
        /// Provides a single <c>IsSuperAdminTicket = true</c> row pointing at
        /// a known <see cref="ApplicationUser"/>.
        /// </summary>
        public void SetupSuperAdmin()
        {
            var row = new TicketUserCompany
            {
                CompanyId = Defaults.CompanyId,
                UserId = Defaults.SuperAdminUserId,
                IsSuperAdminTicket = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord
            };
            TicketUserCompanyRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(new[] { row });
            UserRepositoryMock.Setup(x => x.GetAsync(Defaults.SuperAdminUserId))
                .ReturnsAsync(new ApplicationUser { Id = Defaults.SuperAdminUserId, FirstName = "Ada", LastName = "Admin" });
        }

        /// <summary>
        /// Wires the persisted <see cref="TicketEntity"/> and captures it for
        /// per-test assertions.
        /// </summary>
        public TicketEntity SetupTicketInsertCapture()
        {
            TicketRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketEntity>()))
                .Callback<TicketEntity>(t => CapturedTicket = t)
                .ReturnsAsync(true);

            // TicketCodeGenerator reads (CompanyId, Code-prefix) tickets.
            TicketRepositoryMock.Setup(x => x.GetAllIncludingDeletedAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<TicketEntity, bool>>>()))
                .ReturnsAsync(Array.Empty<TicketEntity>());

            // TicketCompanyDefault provides the three required defaults.
            TicketCompanyDefaultRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(new[]
                {
                    new TicketCompanyDefault
                    {
                        CompanyId = Defaults.CompanyId,
                        TicketStatusDefaultId = Defaults.StatusId,
                        TicketComplexityDefaultId = Defaults.ComplexityId,
                        TimeUnitDefaultId = Defaults.TimeUnitId,
                        GcRecord = BaseAuditableEntity.ActiveGcRecord
                    }
                });

            // Channels + assemblers need a valid channel entity.
            ChannelRepositoryMock.Setup(x => x.GetAsync(Defaults.ChannelId))
                .ReturnsAsync(new CommunicationChannel
                {
                    Name = "WhatsApp",
                    Code = CommunicationChannelCodes.WhatsApp,
                    IsActive = true,
                    GcRecord = BaseAuditableEntity.ActiveGcRecord
                });

            DefaultCatalogRepos();

            return CapturedTicket!;
        }

        /// <summary>
        /// Wires <see cref="TicketAttachmentSettings"/> and the storage service
        /// to match the supplied <paramref name="scenario"/>.
        /// </summary>
        public void SetupAttachmentPersistence(AttachmentScenario scenario)
        {
            var companyId = Defaults.CompanyId;
            var settings = new TicketAttachmentSettingsEntity
            {
                CompanyId = companyId,
                AllowedDocumentTypes = DocumentType.Pdf,
                MaxFileSizeBytes = 1024,
                MaxFilesPerTicket = 100,
                MaxFilesPerDay = null,
                GcRecord = BaseAuditableEntity.ActiveGcRecord
            };
            SettingsRepositoryMock.Setup(x => x.GetAllAsync())
                .ReturnsAsync(new[] { settings });

            if (scenario == AttachmentScenario.SavesAndPersists)
            {
                FileStorageServiceMock
                    .Setup(x => x.SaveAsync(
                        It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Stream _, string key, string _, CancellationToken _) =>
                        new FileStorageSaveResult(key, 1024));
                DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
            }
        }

        /// <summary>
        /// Wires each catalogue repository <see cref="TicketDtoAssembler"/>
        /// depends on, returning non-null entities for the IDs it might look
        /// up. Keeping them here lets every test that uses <c>SetupTicketInsertCapture</c>
        /// share a single configuration.
        /// </summary>
        private void DefaultCatalogRepos()
        {
            var status = new TicketStatus { Name = "Abierto", CompanyId = Defaults.CompanyId };
            EntityIdSetter.SetId(status, Defaults.StatusId);
            SetupRepo(TicketRepositoryMock); // keep the original
            // Re-expose the captured-status repository via a dedicated mock so we don't double-bind TicketEntity.
            var statusRepoMock = new Mock<IGenericRepository<TicketStatus>>();
            statusRepoMock.Setup(x => x.GetAsync(Defaults.StatusId)).ReturnsAsync(status);
            statusRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(status);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketStatus>()).Returns(statusRepoMock.Object);

            var complexity = new TicketComplexity { Name = "Media", ResolutionTimeUnits = 8 };
            EntityIdSetter.SetId(complexity, Defaults.ComplexityId);
            var complexityRepoMock = new Mock<IGenericRepository<TicketComplexity>>();
            complexityRepoMock.Setup(x => x.GetAsync(Defaults.ComplexityId)).ReturnsAsync(complexity);
            complexityRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(complexity);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketComplexity>()).Returns(complexityRepoMock.Object);

            var timeUnit = new TimeUnit { Name = "Horas", Code = 1 };
            EntityIdSetter.SetId(timeUnit, Defaults.TimeUnitId);
            var timeUnitRepoMock = new Mock<IGenericRepository<TimeUnit>>();
            timeUnitRepoMock.Setup(x => x.GetAsync(Defaults.TimeUnitId)).ReturnsAsync(timeUnit);
            timeUnitRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(timeUnit);
            UnitOfWorkMock.Setup(x => x.GetRepository<TimeUnit>()).Returns(timeUnitRepoMock.Object);

            var ticketLogRepoMock = new Mock<IGenericRepository<TicketLog>>();
            ticketLogRepoMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketLog>());
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketLog>()).Returns(ticketLogRepoMock.Object);

            var projectRepoMock = new Mock<IGenericRepository<Project>>();
            projectRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Project?)null);
            projectRepoMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Project>());
            UnitOfWorkMock.Setup(x => x.GetRepository<Project>()).Returns(projectRepoMock.Object);

            var areaRepoMock = new Mock<IGenericRepository<Area>>();
            areaRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Area?)null);
            areaRepoMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<Area>());
            UnitOfWorkMock.Setup(x => x.GetRepository<Area>()).Returns(areaRepoMock.Object);

            var personRepoMock = new Mock<IGenericRepository<Person>>();
            personRepoMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((Person?)null);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(personRepoMock.Object);
        }

        private void SetupRepo<T>(Mock<IGenericRepository<T>> mock) where T : class
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(mock.Object);
        }
    }

    /// <summary>
    /// Stable ids reused across tests so assertions and mock setups align.
    /// </summary>
    private sealed class ScenarioDefaults
    {
        public Guid CompanyId { get; } = Guid.NewGuid();
        public Guid ChannelId { get; } = Guid.NewGuid();
        public Guid SuperAdminUserId { get; } = Guid.NewGuid();
        public Guid StatusId { get; } = Guid.NewGuid();
        public Guid ComplexityId { get; } = Guid.NewGuid();
        public Guid TimeUnitId { get; } = Guid.NewGuid();
        public string SenderIdentifier { get; } = "+34600000000";
        public string SenderDisplayName { get; } = "Remitente Anonimo";
        public string Subject { get; } = "Subject";
        public string Body { get; } = "Body of the message";
        public string ExternalMessageId { get; } = "SM1234567890";
    }
}
