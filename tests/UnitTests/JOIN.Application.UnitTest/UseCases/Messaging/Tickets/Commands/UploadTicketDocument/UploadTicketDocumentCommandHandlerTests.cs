using System.Text;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using MediatR;
using Moq;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;
using TicketEntity = JOIN.Domain.Messaging.Ticket;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

/// <summary>
/// Unit tests for <see cref="UploadTicketDocumentCommandHandler"/>. Exercises
/// the gate sequence defined in the spec section "UploadTicketDocumentCommandHandler
/// — flujo completo" (12 branches).
/// </summary>
public sealed class UploadTicketDocumentCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new UploadTestContext(companyId: Guid.Empty, currentUserId: _fixture.Create<Guid>());
        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserIdIsInvalid_ShouldReturnUserRequiredError()
    {
        var context = new UploadTestContext(companyId: _fixture.Create<Guid>(), currentUserId: Guid.Empty);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("not-a-guid");
        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenTicketNotFound_ShouldReturnTicketNotFoundError()
    {
        var context = SetupHappyPath(Guid.NewGuid(), Guid.NewGuid());
        context.TicketRepositoryMock.Setup(x => x.GetAsync(context.TicketId)).ReturnsAsync((TicketEntity?)null);

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_NOT_FOUND");
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenExplicitTicketLogsIdBelongsToDifferentTicket_ShouldReturnInvalidTicketLogError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = SetupHappyPath(companyId, _fixture.Create<Guid>());
        context.TicketRepositoryMock.Setup(x => x.GetAsync(context.TicketId))
            .ReturnsAsync(CreateTicket(context.TicketId, companyId));

        var wrongLogId = _fixture.Create<Guid>();
        var wrongLog = new TicketLog { TicketId = _fixture.Create<Guid>(), CompanyId = companyId, GcRecord = 0 };
        SetEntityId(wrongLog, wrongLogId);
        context.TicketLogRepositoryMock.Setup(x => x.GetAsync(wrongLogId))
            .ReturnsAsync(wrongLog);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = wrongLogId,
            File = CreateAttachment("report.pdf", 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_LOG");
    }

    [Fact]
    public async Task Handle_WhenTicketLogsIdOmitted_ShouldResolveToLatestActiveLog()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var context = SetupHappyPath(companyId, currentUserId);

        var olderLog = new TicketLog { TicketId = context.TicketId, CompanyId = companyId, GcRecord = 0, Created = DateTime.UtcNow.AddMinutes(-10) };
        var newerLog = new TicketLog { TicketId = context.TicketId, CompanyId = companyId, GcRecord = 0, Created = DateTime.UtcNow };
        var otherLog = new TicketLog { TicketId = _fixture.Create<Guid>(), CompanyId = companyId, GcRecord = 0, Created = DateTime.UtcNow };
        var deletedLog = new TicketLog { TicketId = context.TicketId, CompanyId = companyId, GcRecord = 1, Created = DateTime.UtcNow.AddMinutes(-5) };
        SetEntityId(olderLog, _fixture.Create<Guid>());
        SetEntityId(newerLog, _fixture.Create<Guid>());
        SetEntityId(otherLog, _fixture.Create<Guid>());
        SetEntityId(deletedLog, _fixture.Create<Guid>());

        context.TicketRepositoryMock.Setup(x => x.GetAsync(context.TicketId))
            .ReturnsAsync(CreateTicket(context.TicketId, companyId));
        context.TicketLogRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { olderLog, newerLog, otherLog, deletedLog });

        var capturedEntity = new TicketDocument();
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>()))
            .Callback<TicketDocument>(e => SetEntityId(capturedEntity = e, _fixture.Create<Guid>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null, // omitted — resolver kicks in
            File = CreateAttachment("report.pdf", 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedEntity.TicketLogsId.Should().Be(newerLog.Id); // latest active, ignoring other tickets + soft-deleted
    }

    [Fact]
    public async Task Handle_WhenNoTicketAttachmentSettings_ShouldReturnAttachmentSettingsNotConfiguredError()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var context = SetupHappyPath(companyId, currentUserId);
        context.SettingsRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketAttachmentSettingsEntity>());

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ATTACHMENT_SETTINGS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task Handle_WhenExtensionIsUnsupported_ShouldReturnUnsupportedDocumentTypeError()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment("payload.exe", 512) // not in the spec mapping
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UNSUPPORTED_DOCUMENT_TYPE");
    }

    [Fact]
    public async Task Handle_WhenDocumentTypeIsRecognizedButNotAllowed_ShouldReturnDocumentTypeNotAllowedError()
    {
        // Allowed: Pdf only. Submitted: Image (recognized extension, wrong bit).
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment("photo.png", 512)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DOCUMENT_TYPE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Handle_WhenFileExceedsMaxSize_ShouldReturnFileTooLargeError()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment("report.pdf", 4096) // > 1024
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("FILE_TOO_LARGE");
    }

    [Fact]
    public async Task Handle_WhenMaxFilesPerTicketReached_ShouldReturn409Error()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 2, maxPerDay: null);
        context.DocumentRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                NewActiveDoc(context.TicketId),
                NewActiveDoc(context.TicketId)
            });

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("MAX_FILES_PER_TICKET_REACHED");
    }

    [Fact]
    public async Task Handle_WhenMaxFilesPerDayReached_ShouldReturn429Error()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: 1);
        context.DocumentRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[]
            {
                NewActiveDoc(context.TicketId, context.CompanyId, createdAt: DateTime.UtcNow.AddMinutes(-5))
            });

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DAILY_ATTACHMENT_QUOTA_REACHED");
    }

    [Fact]
    public async Task Handle_WhenMaxFilesPerDayIsNull_ShouldNeverShortCircuitByQuota()
    {
        // Per-ticket cap raised to 300 so the daily check is the only gate;
        // a flood of 250 docs in the same day must NOT block the upload.
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 300, maxPerDay: null);
        var flood = Enumerable.Range(0, 250)
            .Select(_ => NewActiveDoc(context.TicketId, context.CompanyId, createdAt: DateTime.UtcNow))
            .ToArray();
        context.DocumentRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(flood);

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Covers the explicit <c>TicketLogsId</c> happy path: when the caller
    /// supplies a valid log that belongs to the right ticket in the right
    /// tenant, the handler skips the "resolve latest log" branch.
    /// </summary>
    [Fact]
    public async Task Handle_WhenExplicitTicketLogsIdIsValid_ShouldUseItAndContinue()
    {
        var companyId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var context = new UploadTestContext(companyId, currentUserId);
        var ticketId = Guid.NewGuid();
        var ticket = CreateTicket(ticketId, companyId);
        SetEntityId(ticket, ticketId);
        context.TicketId = ticketId;

        var explicitLog = new TicketLog
        {
            TicketId = ticketId,
            CompanyId = companyId,
            GcRecord = 0,
            Created = DateTime.UtcNow.AddMinutes(-3)
        };
        SetEntityId(explicitLog, Guid.NewGuid());

        context.TicketRepositoryMock.Setup(x => x.GetAsync(ticketId)).ReturnsAsync(ticket);
        context.TicketLogRepositoryMock.Setup(x => x.GetAsync(explicitLog.Id)).ReturnsAsync(explicitLog);

        var settings = new TicketAttachmentSettingsEntity
        {
            CompanyId = companyId,
            AllowedDocumentTypes = DocumentType.Pdf,
            MaxFileSizeBytes = 1024,
            MaxFilesPerTicket = 10,
            MaxFilesPerDay = null,
            GcRecord = 0
        };
        context.SettingsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { settings });

        context.DocumentRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketDocument>());
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 512));
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = ticketId,
            TicketLogsId = explicitLog.Id,
            File = CreateAttachment("report.pdf", 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CreatedByUserId.Should().Be(currentUserId);
        response.Data.CreatedByUserName.Should().Be("Ana Torres");
    }

    /// <summary>
    /// Covers the "ticket has no active log" branch when <c>TicketLogsId</c>
    /// is omitted — handler returns INVALID_TICKET_LOG before any other gate.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTicketLogsIdOmittedAndTicketHasNoActiveLogs_ShouldReturnInvalidTicketLogError()
    {
        var companyId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var context = SetupHappyPath(companyId, currentUserId);

        // Override the default "SetupHappyPath" log with an empty list.
        context.TicketLogRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(Array.Empty<TicketLog>());

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment("report.pdf", 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_TICKET_LOG");
        context.FileStorageServiceMock.Verify(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Covers the persistence-failure branch: SaveChanges returns 0 after
    /// a successful file write — handler returns UPLOAD_FAILED (400).
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZeroAfterFileWrite_ShouldReturnUploadFailedError()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 1024));
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPLOAD_FAILED");
    }

    /// <summary>
    /// Covers the actor-lookup null branch: when the uploaded-by user is
    /// not found (e.g. deleted account between token issuance and upload),
    /// the handler still returns success with an empty display name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActorUserNotFound_ShouldStillReturnSuccessWithEmptyDisplayName()
    {
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 1024));
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync((ApplicationUser?)null);

        var handler = context.CreateHandler();
        var request = CreateValidCommand(context.TicketId);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.CreatedByUserName.Should().BeNull();
    }

    /// <summary>
    /// Covers every branch of <c>TryMapExtension</c> (extension → DocumentType).
    /// Each row corresponds to one switch arm in the handler; the resulting
    /// DocumentType must round-trip to the expected enum value.
    /// </summary>
    [Theory]
    [InlineData("report.pdf", DocumentType.Pdf)]
    [InlineData("memo.doc", DocumentType.Word)]
    [InlineData("memo.docx", DocumentType.Word)]
    [InlineData("sheet.xls", DocumentType.Excel)]
    [InlineData("sheet.xlsx", DocumentType.Excel)]
    [InlineData("notes.txt", DocumentType.Text)]
    [InlineData("data.csv", DocumentType.Text)]
    [InlineData("photo.png", DocumentType.Image)]
    [InlineData("photo.jpg", DocumentType.Image)]
    [InlineData("photo.jpeg", DocumentType.Image)]
    [InlineData("anim.gif", DocumentType.Image)]
    [InlineData("bundle.zip", DocumentType.Other)]
    public async Task Handle_WhenFileExtensionMatchesSpecTable_ShouldResolveCorrectDocumentType(
        string fileName, DocumentType expectedType)
    {
        var context = SetupHappyPathWithSettings(allowedTypes: expectedType, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 1024));

        var capturedDoc = new TicketDocument();
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>()))
            .Callback<TicketDocument>(d => capturedDoc = d)
            .ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>()))
            .ReturnsAsync(new ApplicationUser { Id = Guid.NewGuid(), FirstName = "A", LastName = "B" });

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment(fileName, 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedDoc.DocumentType.Should().Be(expectedType);
    }

    [Fact]
    public async Task Handle_WhenAllGatesPass_ShouldPersistFileWithExpectedStorageKeyAndTicketDocument()
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var context = SetupHappyPathWithSettings(allowedTypes: DocumentType.Pdf, maxBytes: 1024, maxPerTicket: 10, maxPerDay: null);

        string? capturedKey = null;
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, CancellationToken>((_, key, _, _) => capturedKey = key)
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) =>
                new FileStorageSaveResult(key, 1024));

        var capturedDoc = new TicketDocument();
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>()))
            .Callback<TicketDocument>(d => capturedDoc = d)
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var request = new UploadTicketDocumentCommand
        {
            TicketId = context.TicketId,
            TicketLogsId = null,
            File = CreateAttachment("report.pdf", 1024)
        };

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Ticket document uploaded successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.OriginalName.Should().Be("report.pdf");
        response.Data.SizeBytes.Should().Be(1024);

        capturedKey.Should().NotBeNull();
        capturedKey!.Should().StartWith($"{context.CompanyId}/{context.TicketId}/");
        capturedKey.Should().EndWith(".pdf");

        capturedDoc.TicketId.Should().Be(context.TicketId);
        capturedDoc.DocumentType.Should().Be(DocumentType.Pdf);
        capturedDoc.StorageProvider.Should().Be(StorageProviderKind.Local);
        capturedDoc.Path.Should().Be(capturedKey);
        capturedDoc.NewName.Should().EndWith(".pdf");
    }

    private static UploadTicketDocumentCommand CreateValidCommand(Guid ticketId) => new()
    {
        TicketId = ticketId,
        TicketLogsId = null,
        File = CreateAttachment("report.pdf", 512)
    };

    private static InboundAttachment CreateAttachment(string fileName, long length) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes("x")), fileName, "application/pdf", length);

    private static TicketEntity CreateTicket(Guid ticketId, Guid companyId)
    {
        var ticket = new TicketEntity
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
            CreatedByUserId = Guid.NewGuid()
        };
        SetEntityId(ticket, ticketId);
        return ticket;
    }

    private static TicketDocument NewActiveDoc(Guid ticketId, Guid companyId = default, DateTime? createdAt = null)
    {
        var doc = new TicketDocument
        {
            TicketId = ticketId,
            CompanyId = companyId == Guid.Empty ? Guid.NewGuid() : companyId,
            TicketLogsId = Guid.NewGuid(),
            DocumentType = DocumentType.Pdf,
            OriginalName = "old.pdf",
            NewName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_x.pdf",
            Path = "x/y.pdf",
            StorageProvider = StorageProviderKind.Local,
            ContentType = "application/pdf",
            SizeBytes = 100,
            GcRecord = 0,
            Created = createdAt ?? DateTime.UtcNow.AddMinutes(-30)
        };
        SetEntityId(doc, Guid.NewGuid());
        return doc;
    }

    private static void SetEntityId(JOIN.Domain.Audit.BaseEntity entity, Guid id)
        => typeof(JOIN.Domain.Audit.BaseEntity).GetProperty(nameof(JOIN.Domain.Audit.BaseEntity.Id))!.SetValue(entity, id);

    private UploadTestContext SetupHappyPath(Guid companyId, Guid currentUserId)
    {
        var context = new UploadTestContext(companyId, currentUserId);
        var ticketId = _fixture.Create<Guid>();

        var ticket = CreateTicket(ticketId, companyId);
        SetEntityId(ticket, ticketId);
        context.TicketId = ticketId;
        context.TicketRepositoryMock.Setup(x => x.GetAsync(ticketId)).ReturnsAsync(ticket);

        // Default: a recent active log so most tests pass step 4.
        var recentLog = new TicketLog { TicketId = ticketId, CompanyId = companyId, GcRecord = 0, Created = DateTime.UtcNow };
        SetEntityId(recentLog, _fixture.Create<Guid>());
        context.TicketLogRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(recentLog);
        context.TicketLogRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync(new[] { recentLog });

        var settings = new TicketAttachmentSettingsEntity
        {
            CompanyId = companyId,
            AllowedDocumentTypes = DocumentType.Pdf,
            MaxFileSizeBytes = 1024,
            MaxFilesPerTicket = 10,
            MaxFilesPerDay = null,
            GcRecord = 0
        };
        context.SettingsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { settings });

        context.DocumentRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(Array.Empty<TicketDocument>());
        context.FileStorageServiceMock
            .Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 512));
        context.DocumentRepositoryMock.Setup(x => x.InsertAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        context.UserRepositoryMock.Setup(x => x.GetAsync(currentUserId))
            .ReturnsAsync(new ApplicationUser { Id = currentUserId, FirstName = "Ana", LastName = "Torres" });

        return context;
    }

    private UploadTestContext SetupHappyPathWithSettings(DocumentType allowedTypes, long maxBytes, int maxPerTicket, int? maxPerDay)
    {
        var companyId = _fixture.Create<Guid>();
        var currentUserId = _fixture.Create<Guid>();
        var context = SetupHappyPath(companyId, currentUserId);

        var settings = new TicketAttachmentSettingsEntity
        {
            CompanyId = companyId,
            AllowedDocumentTypes = allowedTypes,
            MaxFileSizeBytes = maxBytes,
            MaxFilesPerTicket = maxPerTicket,
            MaxFilesPerDay = maxPerDay,
            GcRecord = 0
        };
        context.SettingsRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync(new[] { settings });

        return context;
    }

    /// <summary>
    /// Wires every repository the handler (or its dependencies) reads from.
    /// </summary>
    private sealed class UploadTestContext
    {
        public UploadTestContext(Guid companyId, Guid currentUserId)
        {
            CompanyId = companyId;
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(currentUserId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketEntity>()).Returns(TicketRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketLog>()).Returns(TicketLogRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketAttachmentSettingsEntity>()).Returns(SettingsRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TicketDocument>()).Returns(DocumentRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(UserRepositoryMock.Object);
        }

        public Guid CompanyId { get; }
        public Guid TicketId { get; set; }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketEntity>> TicketRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketLog>> TicketLogRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketAttachmentSettingsEntity>> SettingsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TicketDocument>> DocumentRepositoryMock { get; } = new();
        public Mock<IGenericRepository<ApplicationUser>> UserRepositoryMock { get; } = new();

        public IRequestHandler<UploadTicketDocumentCommand, Response<TicketDocumentDto>> CreateHandler()
            => new UploadTicketDocumentCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object, FileStorageServiceMock.Object);
    }
}
