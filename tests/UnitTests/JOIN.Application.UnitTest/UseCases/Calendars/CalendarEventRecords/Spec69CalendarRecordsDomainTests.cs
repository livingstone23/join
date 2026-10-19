using FluentAssertions;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — domain shape of the new entities: the comment / document fields, the attachment settings
/// defaults, and the bitácora action enum extension. These are pure shape assertions; the handlers and
/// validators live in the next steps.
/// </summary>
public sealed class Spec69CalendarRecordsDomainTests
{
    /// <summary>
    /// Comments cap the body at 2000 chars and the delete reason at 500 — the API validation reaches the
    /// same caps so a comment cannot bypass the limit by skipping the API.
    /// </summary>
    [Fact]
    public void CalendarEventComment_ExposesBodyAndReasonLengthCaps()
    {
        CalendarEventComment.MaxBodyLength.Should().Be(2000);
        CalendarEventComment.MaxDeleteReasonLength.Should().Be(500);

        var comment = new CalendarEventComment();
        comment.Body.Should().BeEmpty();
        comment.DeleteReason.Should().BeNull();
        comment.CreatedUtc.Should().Be(default);
    }

    /// <summary>
    /// Documents carry the same storage fields as leads (SPEC 62): original / new name, path, provider, mime
    /// type and size. The cap on names and path matches <c>LeadDocument</c>.
    /// </summary>
    [Fact]
    public void CalendarEventDocument_ExposesStorageFieldsAndLengthCaps()
    {
        CalendarEventDocument.MaxNameLength.Should().Be(150);
        CalendarEventDocument.MaxPathLength.Should().Be(500);

        var document = new CalendarEventDocument();
        document.DocumentType.Should().Be(DocumentType.None);
        document.OriginalName.Should().BeEmpty();
        document.NewName.Should().BeEmpty();
        document.Path.Should().BeEmpty();
        document.StorageProvider.Should().Be(default);
        document.ContentType.Should().BeEmpty();
        document.SizeBytes.Should().Be(0L);
        document.CalendarEventCommentId.Should().BeNull();
    }

    /// <summary>
    /// <see cref="CalendarAttachmentSettings"/> defaults: PDF / Word / Excel / Image, 10 MB, 5 per comment,
    /// 200 per day — the seed and the spec require these out of the box.
    /// </summary>
    [Fact]
    public void CalendarAttachmentSettings_HasSpecDefaults()
    {
        CalendarAttachmentSettings.DefaultMaxFileSizeBytes.Should().Be(10L * 1024 * 1024);
        CalendarAttachmentSettings.DefaultMaxFilesPerComment.Should().Be(5);
        CalendarAttachmentSettings.DefaultMaxFilesPerDay.Should().Be(200);
        CalendarAttachmentSettings.DefaultAllowedTypes.Should().Be(
            DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image);

        var settings = new CalendarAttachmentSettings();
        settings.AllowedDocumentTypes.Should().Be(CalendarAttachmentSettings.DefaultAllowedTypes);
        settings.MaxFileSizeBytes.Should().Be(CalendarAttachmentSettings.DefaultMaxFileSizeBytes);
        settings.MaxFilesPerComment.Should().Be(CalendarAttachmentSettings.DefaultMaxFilesPerComment);
        settings.MaxFilesPerDay.Should().Be(CalendarAttachmentSettings.DefaultMaxFilesPerDay);
    }

    /// <summary>
    /// The bitácora gains the comment and document actions with stable ids — the API uses them to flag the
    /// kind of change in the "Historial" tab (FE-44).
    /// </summary>
    [Fact]
    public void CalendarEventLogAction_AddsCommentAndDocumentActions()
    {
        ((int)CalendarEventLogAction.CommentAdded).Should().Be(9);
        ((int)CalendarEventLogAction.DocumentAdded).Should().Be(10);
        ((int)CalendarEventLogAction.CommentDeleted).Should().Be(11);
        ((int)CalendarEventLogAction.DocumentDeleted).Should().Be(12);

        // Existing ids stay untouched so old bitácora rows keep their action type.
        ((int)CalendarEventLogAction.Created).Should().Be(1);
        ((int)CalendarEventLogAction.Deleted).Should().Be(8);
    }

    /// <summary>
    /// <see cref="CalendarEventLog"/> exposes the denormalized comment / document ids (SPEC 69 rule 6) and the
    /// navigation properties that point at them.
    /// </summary>
    [Fact]
    public void CalendarEventLog_ExposesCommentAndDocumentReferences()
    {
        var log = new CalendarEventLog();
        log.CalendarEventCommentId.Should().BeNull();
        log.CalendarEventDocumentId.Should().BeNull();
        log.Notes.Should().BeNull();

        var commentProp = typeof(CalendarEventLog).GetProperty(nameof(CalendarEventLog.CalendarEventComment));
        var documentProp = typeof(CalendarEventLog).GetProperty(nameof(CalendarEventLog.CalendarEventDocument));
        commentProp.Should().NotBeNull();
        documentProp.Should().NotBeNull();
    }
}