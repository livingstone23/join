using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;
using Moq;
using AttachmentSettingsEntity = JOIN.Domain.Calendars.CalendarAttachmentSettings;
using DocEntity = JOIN.Domain.Calendars.CalendarEventDocument;
using CommentEntity = JOIN.Domain.Calendars.CalendarEventComment;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the upload handler walks every limit in rule 7 (settings row, allowed types, file size,
/// per-comment cap, daily cap) before persisting the file. Each path is mapped to its own error code so the
/// UI can show the right banner.
/// </summary>
public sealed class UploadCalendarEventDocumentCommandHandlerTests
{
    private const string FileContentType = "application/pdf";

    /// <summary> No settings row for the company → <c>CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>. </summary>
    [Fact]
    public async Task Handle_WhenSettingsMissing_RespondsAttachmentSettingsNotConfigured()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(Array.Empty<AttachmentSettingsEntity>());
        var storage = NewStorageResolver();
        var handler = NewHandler(context, storage.Resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.AttachmentSettingsNotConfigured);
    }

    /// <summary> Extension off the table → <c>CALENDAR_DOCUMENT_TYPE_NOT_ALLOWED</c>. </summary>
    [Fact]
    public async Task Handle_WhenExtensionIsUnknown_RespondsDocumentTypeNotAllowed()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "asset.gif", contentType: "image/gif"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentTypeNotAllowed);
    }

    /// <summary> Extension is on the table but the company forbids that type → same code. </summary>
    [Fact]
    public async Task Handle_WhenTypeIsNotInCompanyAllowList_RespondsDocumentTypeNotAllowed()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var settings = Tweak(DefaultSettings(context), s => s.AllowedDocumentTypes = DocumentType.Pdf);
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { settings });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "sheet.xlsx"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentTypeNotAllowed);
    }

    /// <summary> File exceeds the per-row cap → <c>CALENDAR_DOCUMENT_TOO_LARGE</c>. </summary>
    [Fact]
    public async Task Handle_WhenFileExceedsMaxSize_RespondsDocumentTooLarge()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var settings = Tweak(DefaultSettings(context), s => s.MaxFileSizeBytes = 16);
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { settings });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf", content: new byte[64]), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentTooLarge);
    }

    /// <summary> Comment id of a different activity → <c>CALENDAR_COMMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenCommentIdIsFromAnotherEvent_RespondsCommentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        var comment = new CommentEntity
        {
            CompanyId = context.CompanyId,
            CalendarEventId = Guid.NewGuid(),
            AuthorUserId = context.ActorUserId,
            Body = "stale",
            CreatedUtc = DateTime.UtcNow
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf", commentId: comment.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CommentNotFound);
    }

    /// <summary> Already <c>MaxFilesPerComment</c> live documents → <c>CALENDAR_DOCUMENT_LIMIT_PER_COMMENT</c>. </summary>
    [Fact]
    public async Task Handle_WhenCommentAlreadyAtLimit_RespondsDocumentLimitPerComment()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var settings = Tweak(DefaultSettings(context), s => s.MaxFilesPerComment = 1);
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { settings });
        var comment = new CommentEntity
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "limit",
            CreatedUtc = DateTime.UtcNow
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });
        var existingDoc = new DocEntity
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            CalendarEventCommentId = comment.Id,
            DocumentType = DocumentType.Pdf,
            OriginalName = "x.pdf",
            NewName = "x.pdf",
            Path = "x",
            ContentType = FileContentType,
            SizeBytes = 1,
            UploadedByUserId = context.ActorUserId,
            Created = DateTime.UtcNow
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { existingDoc });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf", commentId: comment.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentLimitPerComment);
    }

    /// <summary> Daily quota reached → <c>CALENDAR_DOCUMENT_LIMIT_PER_DAY</c>. </summary>
    [Fact]
    public async Task Handle_WhenDailyQuotaReached_RespondsDocumentLimitPerDay()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var settings = Tweak(DefaultSettings(context), s => s.MaxFilesPerDay = 1);
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { settings });
        var earlierToday = new DocEntity
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            DocumentType = DocumentType.Pdf,
            OriginalName = "yesterday.pdf",
            NewName = "yesterday.pdf",
            Path = "y",
            ContentType = FileContentType,
            SizeBytes = 1,
            UploadedByUserId = context.ActorUserId,
            Created = DateTime.UtcNow.Date
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { earlierToday });
        var handler = NewHandler(context, NewStorageResolver().Resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentLimitPerDay);
    }

    /// <summary> Happy path — the handler persists the file through the resolver and logs <c>DocumentAdded</c>. </summary>
    [Fact]
    public async Task Handle_WhenAllChecksPass_PersistsFileAndLogsDocumentAdded()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarAttachmentSettingsRepositoryMock.SetupRows(new[] { DefaultSettings(context) });
        context.TrackWrites();

        var (resolver, currentMock) = NewStorageResolver();
        var handler = NewHandler(context, resolver);

        var response = await handler.Handle(NewUpload(context, "doc.pdf"), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var document = context.CapturedDocumentInserts.Should().ContainSingle().Subject;
        document.CompanyId.Should().Be(context.CompanyId);
        document.CalendarEventId.Should().Be(context.EventId);
        document.DocumentType.Should().Be(DocumentType.Pdf);
        document.UploadedByUserId.Should().Be(context.ActorUserId);
        document.StorageProvider.Should().Be(StorageProviderKind.Local);
        document.Path.Should().StartWith($"calendar/{context.CompanyId}/{context.EventId}/");

        currentMock.Verify(x => x.SaveAsync(
            It.IsAny<Stream>(),
            It.Is<string>(k => k.StartsWith($"calendar/{context.CompanyId}/{context.EventId}/")),
            FileContentType,
            It.IsAny<CancellationToken>()), Times.Once);

        var log = context.CapturedLogs.Should().ContainSingle().Subject;
        log.Action.Should().Be(CalendarEventLogAction.DocumentAdded);
        log.CalendarEventDocumentId.Should().Be(document.Id);
        log.CalendarEventCommentId.Should().BeNull();
        log.ChannelId.Should().Be(context.WebChannelId);

        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AttachmentSettingsEntity DefaultSettings(Spec69CalendarEventRecordsContext context) => new()
    {
        CompanyId = context.CompanyId,
        AllowedDocumentTypes = DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image,
        MaxFileSizeBytes = 1024,
        MaxFilesPerComment = 5,
        MaxFilesPerDay = 200
    };

    /// <summary> Mutates the settings row in place (entity is a class, <c>with</c> does not apply). </summary>
    private static AttachmentSettingsEntity Tweak(AttachmentSettingsEntity source, Action<AttachmentSettingsEntity> mutate)
    {
        mutate(source);
        return source;
    }

    private static UploadCalendarEventDocumentCommand NewUpload(
        Spec69CalendarEventRecordsContext context,
        string fileName,
        Guid? commentId = null,
        string contentType = FileContentType,
        byte[]? content = null) =>
        new(context.EventId, commentId, fileName, contentType, content ?? [0x25, 0x26]);

    private static (IFileStorageResolver Resolver, Mock<IFileStorageService> CurrentMock) NewStorageResolver()
    {
        var current = new Mock<IFileStorageService>();
        current.SetupGet(x => x.Kind).Returns(StorageProviderKind.Local);
        current.Setup(x => x.SaveAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string key, string _, CancellationToken _) => new FileStorageSaveResult(key, 2));
        var resolverMock = new Mock<IFileStorageResolver>();
        resolverMock.SetupGet(x => x.Current).Returns(current.Object);
        resolverMock.Setup(x => x.Resolve(It.IsAny<StorageProviderKind>())).Returns(current.Object);
        return (resolverMock.Object, current);
    }

    private static UploadCalendarEventDocumentCommandHandler NewHandler(
        Spec69CalendarEventRecordsContext context,
        IFileStorageResolver storage) =>
        new(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            storage,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));
}