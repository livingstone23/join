using FluentAssertions;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — the comments list handler reads with Dapper the activity's comments newest-first, nests each
/// comment's documents and hides the body of the soft-deleted ones while keeping the metadata (deleter, time,
/// reason) for the UI.
/// </summary>
public sealed class ListCalendarEventCommentsQueryHandlerTests
{
    /// <summary> Foreign-tenant probe → <c>CALENDAR_EVENT_NOT_FOUND</c>, without touching the database. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var connection = new FakeSqlConnectionFactory();
        var handler = new ListCalendarEventCommentsQueryHandler(connection, context.Access);

        var response = await handler.Handle(new ListCalendarEventCommentsQuery(context.EventId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
        connection.LastCommandText.Should().BeEmpty();
    }

    /// <summary> A deleted comment returns its metadata with <c>Body = null</c>; documents nest under their comment. </summary>
    [Fact]
    public async Task Handle_WhenCommentsExist_NestsDocumentsAndHidesDeletedBody()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var liveId = Guid.NewGuid();
        var deletedId = Guid.NewGuid();
        var deleterId = Guid.NewGuid();
        var connection = new FakeSqlConnectionFactory();
        connection.SetResults(
            FakeResultSet.FromRows(
                Comment(context, liveId, "second", new DateTime(2027, 4, 5, 12, 0, 0, DateTimeKind.Utc)),
                Comment(context, deletedId, "first", new DateTime(2027, 4, 5, 11, 0, 0, DateTimeKind.Utc),
                    deleterId, "Luis Admin ", new DateTime(2027, 4, 5, 13, 0, 0, DateTimeKind.Utc), "off-topic")),
            FakeResultSet.FromRows(
                Document(context, liveId, "contract.pdf"),
                Document(context, null, "loose.pdf")));

        var handler = new ListCalendarEventCommentsQueryHandler(connection, context.Access);
        var response = await handler.Handle(new ListCalendarEventCommentsQuery(context.EventId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var items = response.Data!;
        items.Should().HaveCount(2);

        var live = items[0];
        live.Body.Should().Be("second");
        live.AuthorUserName.Should().Be("Ana Pérez");
        live.IsDeleted.Should().BeFalse();
        live.DeletedByUserName.Should().BeNull();
        live.Documents.Should().ContainSingle().Which.OriginalName.Should().Be("contract.pdf");

        var deleted = items[1];
        deleted.Body.Should().BeNull();
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeleteReason.Should().Be("off-topic");
        deleted.DeletedByUserId.Should().Be(deleterId);
        deleted.DeletedByUserName.Should().Be("Luis Admin");
        deleted.Documents.Should().BeEmpty();

        connection.LastCommandText.Should().Contain("d.CompanyId = @TenantId").And.Contain("d.GcRecord = 0");
        connection.CapturedParameters["TenantId"].Should().Be(context.CompanyId);
        connection.CapturedParameters["EventId"].Should().Be(context.EventId);
    }

    private static Dictionary<string, object?> Comment(
        Spec69CalendarEventRecordsContext context, Guid id, string body, DateTime createdUtc,
        Guid? deletedBy = null, string deletedByName = " ", DateTime? deletedUtc = null, string? reason = null) => new()
    {
        ["Id"] = id,
        ["CalendarEventId"] = context.EventId,
        ["AuthorUserId"] = context.ActorUserId,
        ["AuthorUserName"] = "Ana Pérez",
        ["CreatedUtc"] = createdUtc,
        ["Body"] = body,
        ["DeletedByUserId"] = deletedBy,
        ["DeletedByUserName"] = deletedByName,
        ["DeletedUtc"] = deletedUtc,
        ["DeleteReason"] = reason
    };

    internal static Dictionary<string, object?> Document(
        Spec69CalendarEventRecordsContext context, Guid? commentId, string name, DateTime? deletedUtc = null) => new()
    {
        ["Id"] = Guid.NewGuid(),
        ["CalendarEventId"] = context.EventId,
        ["CalendarEventCommentId"] = commentId,
        ["DocumentType"] = (int)DocumentType.Pdf,
        ["OriginalName"] = name,
        ["NewName"] = name,
        ["ContentType"] = "application/pdf",
        ["SizeBytes"] = 12L,
        ["UploadedByUserId"] = context.ActorUserId,
        ["UploadedByUserName"] = "Ana Pérez",
        ["CreatedAt"] = new DateTime(2027, 4, 5, 12, 0, 0, DateTimeKind.Utc),
        ["DeletedByUserId"] = deletedUtc is null ? null : Guid.NewGuid(),
        ["DeletedByUserName"] = deletedUtc is null ? " " : "Luis Admin",
        ["DeletedUtc"] = deletedUtc,
        ["DeleteReason"] = deletedUtc is null ? null : "retention"
    };
}
