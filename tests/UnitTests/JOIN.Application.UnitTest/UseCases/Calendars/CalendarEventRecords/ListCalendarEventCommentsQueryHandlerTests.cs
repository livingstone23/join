using FluentAssertions;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Enums;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;
using CalendarEventComment = JOIN.Domain.Calendars.CalendarEventComment;
using CalendarEventDocument = JOIN.Domain.Calendars.CalendarEventDocument;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the comments list handler returns the activity's comments newest-first and hides the body of
/// the soft-deleted ones while keeping the metadata (deleter, time, reason) for the UI.
/// </summary>
public sealed class ListCalendarEventCommentsQueryHandlerTests
{
    /// <summary> Foreign-tenant probe → <c>CALENDAR_EVENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var handler = new ListCalendarEventCommentsQueryHandler(context.UnitOfWorkMock.Object, context.Visibility);

        var response = await handler.Handle(new ListCalendarEventCommentsQuery(context.EventId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary> A deleted comment returns its metadata with <c>Body = null</c> and the documents nested. </summary>
    [Fact]
    public async Task Handle_WhenCommentsExist_ReturnsSortedAndHidesDeletedBody()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var newer = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "second",
            CreatedUtc = new DateTime(2027, 4, 5, 12, 0, 0, DateTimeKind.Utc)
        };
        var older = new CalendarEventComment
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            AuthorUserId = context.ActorUserId,
            Body = "first",
            CreatedUtc = new DateTime(2027, 4, 5, 11, 0, 0, DateTimeKind.Utc),
            DeletedUtc = new DateTime(2027, 4, 5, 13, 0, 0, DateTimeKind.Utc),
            DeletedByUserId = Guid.NewGuid(),
            DeleteReason = "off-topic"
        };
        context.CalendarEventCommentRepositoryMock.SetupRows(new[] { newer, older });
        context.CalendarEventDocumentRepositoryMock.SetupRows(Array.Empty<CalendarEventDocument>());

        var handler = new ListCalendarEventCommentsQueryHandler(context.UnitOfWorkMock.Object, context.Visibility);
        var response = await handler.Handle(new ListCalendarEventCommentsQuery(context.EventId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var items = response.Data!;
        items.Should().HaveCount(2);

        var first = items[0];
        first.Body.Should().Be("second");
        first.IsDeleted.Should().BeFalse();
        first.Documents.Should().BeEmpty();

        var second = items[1];
        second.Body.Should().BeNull();
        second.IsDeleted.Should().BeTrue();
        second.DeleteReason.Should().Be("off-topic");
        second.DeletedByUserId.Should().Be(older.DeletedByUserId);
    }
}