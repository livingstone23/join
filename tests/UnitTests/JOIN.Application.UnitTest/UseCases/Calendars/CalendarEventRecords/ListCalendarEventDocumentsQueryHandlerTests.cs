using FluentAssertions;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the documents list handler reads with Dapper, narrows by comment when supplied, tags soft-deleted
/// rows and carries the uploader's full name. The visibility gate uses the same rule 5 access as the comments
/// handler.
/// </summary>
public sealed class ListCalendarEventDocumentsQueryHandlerTests
{
    /// <summary> Foreign-tenant probe → <c>CALENDAR_EVENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var handler = new ListCalendarEventDocumentsQueryHandler(new FakeSqlConnectionFactory(), context.Access);

        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, null), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary> The <c>commentId</c> filter is added to the SQL and bound as a parameter. </summary>
    [Fact]
    public async Task Handle_WhenCommentIdIsGiven_AppliesFilter()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var commentId = Guid.NewGuid();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(
            ListCalendarEventCommentsQueryHandlerTests.Document(context, commentId, "match.pdf")));

        var handler = new ListCalendarEventDocumentsQueryHandler(connection, context.Access);
        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, commentId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().ContainSingle().Which.OriginalName.Should().Be("match.pdf");
        connection.LastCommandText.Should().Contain("d.CalendarEventCommentId = @CommentId");
        connection.CapturedParameters["CommentId"].Should().Be(commentId);
    }

    /// <summary> Without a filter, live and soft-deleted documents are returned (the DTO tags <c>IsDeleted</c>). </summary>
    [Fact]
    public async Task Handle_WhenCommentIdIsMissing_ReturnsAllLiveAndDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(FakeResultSet.FromRows(
            ListCalendarEventCommentsQueryHandlerTests.Document(context, null, "live.pdf"),
            ListCalendarEventCommentsQueryHandlerTests.Document(context, null, "deleted.pdf", DateTime.UtcNow)));

        var handler = new ListCalendarEventDocumentsQueryHandler(connection, context.Access);
        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().HaveCount(2);
        var live = response.Data!.Single(d => d.OriginalName == "live.pdf");
        live.IsDeleted.Should().BeFalse();
        live.UploadedByUserName.Should().Be("Ana Pérez");
        live.DeletedByUserName.Should().BeNull();
        var deleted = response.Data!.Single(d => d.OriginalName == "deleted.pdf");
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeleteReason.Should().Be("retention");
        deleted.DeletedByUserName.Should().Be("Luis Admin");
        connection.LastCommandText.Should().NotContain("@CommentId");
    }
}
