using FluentAssertions;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;
using CalendarEventComment = JOIN.Domain.Calendars.CalendarEventComment;
using CalendarEventDocument = JOIN.Domain.Calendars.CalendarEventDocument;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the comment delete handler keeps the row (rule 4), stamps the deletion metadata, and writes a
/// <c>CommentDeleted</c> log carrying the reason in <c>Notes</c>. Foreign-tenant and already-deleted probes
/// answer the same code (404 vs 409) so the UI's error message stays honest.
/// </summary>
public sealed class DeleteCalendarEventCommentCommandHandlerTests
{
    /// <summary> Comment id of another tenant → <c>CALENDAR_COMMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenCommentIsMissing_RespondsCommentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarEventCommentRepositoryMock.SetupRows(Array.Empty<CalendarEventComment>());
        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, Guid.NewGuid(), "off-topic"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CommentNotFound);
    }

    /// <summary> Soft-deleted comment → <c>CALENDAR_RECORD_ALREADY_DELETED</c> (409). </summary>
    [Fact]
    public async Task Handle_WhenAlreadyDeleted_RespondsRecordAlreadyDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var comment = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "stale",
            CreatedUtc = DateTime.UtcNow.AddDays(-1),
            DeletedUtc = DateTime.UtcNow.AddHours(-1),
            DeletedByUserId = Guid.NewGuid(),
            DeleteReason = "duplicate"
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });
        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, comment.Id, "again"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.RecordAlreadyDeleted);
    }

    /// <summary> The activity is no longer visible to the deleter (e.g. flag revoked mid-life) → event not found. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var comment = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "alive",
            CreatedUtc = DateTime.UtcNow
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });

        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, comment.Id, "audit"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary> Happy path — soft-delete stamps the row and writes a log row carrying the reason. </summary>
    [Fact]
    public async Task Handle_WhenLiveAndVisible_SoftDeletesAndLogsCommentDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.TrackWrites();
        var comment = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "live",
            CreatedUtc = DateTime.UtcNow.AddDays(-2)
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });

        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, comment.Id, "  audit reason  "),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var deleted = context.CapturedCommentUpdates.Should().ContainSingle().Subject;
        deleted.DeletedUtc.Should().NotBeNull();
        deleted.DeletedByUserId.Should().Be(context.ActorUserId);
        deleted.DeleteReason.Should().Be("audit reason");

        var log = context.CapturedLogs.Should().ContainSingle().Subject;
        log.Action.Should().Be(CalendarEventLogAction.CommentDeleted);
        log.CalendarEventCommentId.Should().Be(comment.Id);
        log.Notes.Should().Be("audit reason");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary> The comment belongs to another activity than the route's → <c>CALENDAR_COMMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenCommentBelongsToAnotherActivity_RespondsCommentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.TrackWrites();
        var comment = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = Guid.NewGuid(),
            AuthorUserId = context.ActorUserId,
            Body = "elsewhere",
            CreatedUtc = DateTime.UtcNow
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });

        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, comment.Id, "audit"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CommentNotFound);
        context.CapturedCommentUpdates.Should().BeEmpty();
    }

    /// <summary> The comment belongs to another company → <c>CALENDAR_COMMENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenCommentBelongsToAnotherCompany_RespondsCommentNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var comment = new CalendarEventComment
        {
            CompanyId = Guid.NewGuid(),
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "foreign",
            CreatedUtc = DateTime.UtcNow
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { comment });

        var handler = new DeleteCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Access,
            new CalendarEventLogWriter(context.UnitOfWorkMock.Object));

        var response = await handler.Handle(
            new DeleteCalendarEventCommentCommand(context.EventId, comment.Id, "audit"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.CommentNotFound);
    }
}
