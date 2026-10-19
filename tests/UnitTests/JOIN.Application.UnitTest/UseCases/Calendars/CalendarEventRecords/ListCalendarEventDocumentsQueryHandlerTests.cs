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
/// SPEC 69 — the documents list handler narrows by comment when supplied, hides soft-deleted rows, and
/// tags the response with the uploader's full name. The visibility gate uses the same helper as the
/// comments handler.
/// </summary>
public sealed class ListCalendarEventDocumentsQueryHandlerTests
{
    /// <summary> Foreign-tenant probe → <c>CALENDAR_EVENT_NOT_FOUND</c>. </summary>
    [Fact]
    public async Task Handle_WhenActivityIsHidden_RespondsEventNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);
        var handler = new ListCalendarEventDocumentsQueryHandler(context.UnitOfWorkMock.Object, context.Visibility);

        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, null), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.EventNotFound);
    }

    /// <summary> The <c>commentId</c> filter narrows the result to that comment's documents only. </summary>
    [Fact]
    public async Task Handle_WhenCommentIdIsGiven_AppliesFilter()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var commentId = Guid.NewGuid();
        var match = new CalendarEventDocument
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            CalendarEventCommentId = commentId,
            DocumentType = DocumentType.Pdf,
            OriginalName = "match.pdf",
            NewName = "match.pdf",
            Path = "p",
            ContentType = "application/pdf",
            SizeBytes = 12,
            UploadedByUserId = context.ActorUserId,
            Created = DateTime.UtcNow
        };
        var other = new CalendarEventDocument
        {
            CompanyId = match.CompanyId,
            CalendarEventId = match.CalendarEventId,
            CalendarEventCommentId = Guid.NewGuid(),
            DocumentType = match.DocumentType,
            OriginalName = "other.pdf",
            NewName = "other.pdf",
            Path = match.Path,
            ContentType = match.ContentType,
            SizeBytes = match.SizeBytes,
            UploadedByUserId = match.UploadedByUserId,
            Created = match.Created
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { match, other });

        var handler = new ListCalendarEventDocumentsQueryHandler(context.UnitOfWorkMock.Object, context.Visibility);
        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, commentId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().ContainSingle().Which.OriginalName.Should().Be("match.pdf");
    }

    /// <summary> Without a filter, both live and soft-deleted documents are returned (the DTO tags <c>IsDeleted</c>). </summary>
    [Fact]
    public async Task Handle_WhenCommentIdIsMissing_ReturnsAllLiveAndDeleted()
    {
        var context = new Spec69CalendarEventRecordsContext();
        var live = new CalendarEventDocument
        {
            CompanyId = context.CompanyId,
            CalendarEventId = context.EventId,
            DocumentType = DocumentType.Pdf,
            OriginalName = "live.pdf",
            NewName = "live.pdf",
            Path = "p",
            ContentType = "application/pdf",
            SizeBytes = 12,
            UploadedByUserId = context.ActorUserId
        };
        var deleted = new CalendarEventDocument
        {
            CompanyId = live.CompanyId,
            CalendarEventId = live.CalendarEventId,
            DocumentType = live.DocumentType,
            OriginalName = "deleted.pdf",
            NewName = live.NewName,
            Path = live.Path,
            ContentType = live.ContentType,
            SizeBytes = live.SizeBytes,
            UploadedByUserId = live.UploadedByUserId,
            Created = live.Created,
            DeletedUtc = DateTime.UtcNow
        };
        context.CalendarEventDocumentRepositoryMock.SetupRows(new[] { live, deleted });

        var handler = new ListCalendarEventDocumentsQueryHandler(context.UnitOfWorkMock.Object, context.Visibility);
        var response = await handler.Handle(new ListCalendarEventDocumentsQuery(context.EventId, null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().HaveCount(2);
        response.Data!.Should().ContainSingle(d => d.OriginalName == "live.pdf" && !d.IsDeleted);
        response.Data!.Should().ContainSingle(d => d.OriginalName == "deleted.pdf" && d.IsDeleted);
    }
}