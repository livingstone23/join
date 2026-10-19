using FluentAssertions;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the comment write handler exercises rule 5 (visibility gates the operation), the WEB channel
/// lookup, the body trim, and the bitácora row carrying the denormalized comment id.
/// </summary>
public sealed class CreateCalendarEventCommentCommandHandlerTests
{
    /// <summary> No resolved user → <c>USER_REQUIRED</c> before the visibility probe runs. </summary>
    [Fact]
    public async Task Handle_WhenUserIdIsNotAGuid_RespondsUserRequired()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(string.Empty);
        var handler = new CreateCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            LogWriter(context));

        var response = await handler.Handle(
            new CreateCalendarEventCommentCommand(context.EventId, "Hello"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.UserRequired);
    }

    /// <summary> Foreign-tenant probe → <c>CALENDAR_EVENT_NOT_FOUND</c> (rule 5). </summary>
    [Fact]
    public async Task Handle_WhenEventIsNotVisible_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var handler = new CreateCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            LogWriter(context));

        var response = await handler.Handle(
            new CreateCalendarEventCommentCommand(context.EventId, "Hello"),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
        context.CapturedCommentInserts.Should().BeEmpty();
    }

    /// <summary> Happy path — comment is inserted, the body is trimmed, and the log row carries the id. </summary>
    [Fact]
    public async Task Handle_WhenEventIsVisible_InsertsAndLogsCommentAdded()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.TrackWrites();
        var author = new ApplicationUser { FirstName = "Ana", LastName = "Pérez" };
        context.ApplicationUserRepositoryMock.Setup(x => x.GetAsync(context.ActorUserId)).ReturnsAsync(author);

        var handler = new CreateCalendarEventCommentCommandHandler(
            context.UnitOfWorkMock.Object,
            context.CurrentUserServiceMock.Object,
            context.Visibility,
            LogWriter(context));

        var response = await handler.Handle(
            new CreateCalendarEventCommentCommand(context.EventId, "  Hello world  "),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var inserted = context.CapturedCommentInserts.Should().ContainSingle().Subject;
        inserted.Body.Should().Be("Hello world");
        inserted.CalendarEventId.Should().Be(context.EventId);
        inserted.AuthorUserId.Should().Be(context.ActorUserId);

        var log = context.CapturedLogs.Should().ContainSingle().Subject;
        log.Action.Should().Be(CalendarEventLogAction.CommentAdded);
        log.CalendarEventCommentId.Should().Be(inserted.Id);
        log.CalendarEventDocumentId.Should().BeNull();
        log.ActorUserId.Should().Be(context.ActorUserId);
        log.ChannelId.Should().Be(context.WebChannelId);

        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static JOIN.Application.UseCases.Calendars.Common.CalendarEventLogWriter LogWriter(Spec69CalendarEventRecordsContext context) =>
        new(context.UnitOfWorkMock.Object);
}