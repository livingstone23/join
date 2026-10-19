using FluentAssertions;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;
using CalendarEventComment = JOIN.Domain.Calendars.CalendarEventComment;
using CalendarEventDocument = JOIN.Domain.Calendars.CalendarEventDocument;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the document delete handler is the symmetric counterpart of the comment delete handler. The
/// file is left untouched (logical delete on the row only) and the bitácora carries the reason.
/// </summary>
public sealed class DeleteCalendarEventDocumentCommandHandlerTests
{
    /// <summary> Document id of another tenant → <c>CALENDAR_DOCUMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenDocumentIsMissing_RespondsDocumentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarEventDocumentRepositoryMock.SetupRows(Array.Empty<CalendarEventDocument>());
        var handler = new DeleteCalendarEventDocumentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventDocumentCommand(Guid.NewGuid(), "audit"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.DocumentNotFound);
    }

    /// <summary> Already soft-deleted → <c>CALENDAR_RECORD_ALREADY_DELETED</c> (409). </summary>
    [Fact]
    public async Task Handle_WhenAlreadyDeleted_RespondsRecordAlreadyDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var document = new CalendarEventDocument
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            DocumentType = DocumentType.Pdf,
            OriginalName = "old.pdf",
            NewName = "old.pdf",
            Path = "calendar/old",
            ContentType = "application/pdf",
            SizeBytes = 1,
            UploadedByUserId = context.ActorUserId,
            DeletedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedByUserId = Guid.NewGuid(),
            DeleteReason = "previous"
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { document });
        var handler = new DeleteCalendarEventDocumentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventDocumentCommand(document.Id, "again"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RecordAlreadyDeleted);
    }

    /// <summary> Happy path — file stays on disk, the row is marked deleted, and the log records the reason. </summary>
    [Fact]
    public async Task Handle_WhenLiveAndVisible_SoftDeletesAndLogsDocumentDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.TrackWrites();
        var document = new CalendarEventDocument
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            DocumentType = DocumentType.Pdf,
            OriginalName = "live.pdf",
            NewName = "live.pdf",
            Path = "calendar/live",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            UploadedByUserId = context.ActorUserId
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { document });

        var handler = new DeleteCalendarEventDocumentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventDocumentCommand(document.Id, "  retention  "),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var deleted = context.CapturedDocumentUpdates.Should().ContainSingle().Subject;
        deleted.DeletedUtc.Should().NotBeNull();
        deleted.DeletedByUserId.Should().Be(context.ActorUserId);
        deleted.DeleteReason.Should().Be("retention");
        // Logical delete — physical file untouched (no resolver call expected).
        deleted.Path.Should().Be("calendar/live");

        var log = context.CapturedLogs.Should().ContainSingle().Subject;
        log.Action.Should().Be(CalendarEventLogAction.DocumentDeleted);
        log.CalendarEventDocumentId.Should().Be(document.Id);
        log.Notes.Should().Be("retention");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}