using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 — focused coverage of the input contracts: validators reject empty / oversized payloads, and the
/// extension → <see cref="DocumentType"/> table is stable so the upload handler never silently misclassifies a
/// file. End-to-end behavior is exercised by the integration tests.
/// </summary>
public sealed class Spec69RecordsValidatorAndExtensionTests
{
    /// <summary>
    /// Comments cap the body at 2000 chars (the entity cap). The validator runs before the handler so an
    /// oversized body fails fast with the standard 400.
    /// </summary>
    [Fact]
    public void CreateCommentValidator_RejectsEmptyAndOversized()
    {
        var validator = new CreateCalendarEventCommentCommandValidator();
        validator.TestValidate(new CreateCalendarEventCommentCommand(Guid.NewGuid(), "")).ShouldHaveValidationErrorFor(x => x.Body);
        validator.TestValidate(new CreateCalendarEventCommentCommand(Guid.NewGuid(), new string('a', 2001))).ShouldHaveValidationErrorFor(x => x.Body);
    }

    [Fact]
    public void CreateCommentValidator_AcceptsMaxLengthBody()
    {
        var validator = new CreateCalendarEventCommentCommandValidator();
        validator.TestValidate(new CreateCalendarEventCommentCommand(Guid.NewGuid(), new string('a', 2000))).IsValid.Should().BeTrue();
    }

    /// <summary>
    /// The reason on comment / document deletes is mandatory (rule 4) and capped at 500 chars.
    /// </summary>
    [Fact]
    public void DeleteCommentValidator_RejectsEmptyReason()
    {
        var validator = new DeleteCalendarEventCommentCommandValidator();
        validator.TestValidate(new DeleteCalendarEventCommentCommand(Guid.NewGuid(), "")).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void DeleteDocumentValidator_RejectsEmptyReason()
    {
        var validator = new DeleteCalendarEventDocumentCommandValidator();
        validator.TestValidate(new DeleteCalendarEventDocumentCommand(Guid.NewGuid(), "")).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    /// <summary>
    /// Document uploads: original name and content are required.
    /// </summary>
    [Fact]
    public void UploadDocumentValidator_RejectsEmptyOriginalNameOrContent()
    {
        var validator = new UploadCalendarEventDocumentCommandValidator();
        validator.TestValidate(new UploadCalendarEventDocumentCommand(Guid.NewGuid(), null, "", "application/pdf", new byte[] { 0x25 }))
            .ShouldHaveValidationErrorFor(x => x.OriginalName);
        validator.TestValidate(new UploadCalendarEventDocumentCommand(Guid.NewGuid(), null, "doc.pdf", "application/pdf", Array.Empty<byte>()))
            .ShouldHaveValidationErrorFor(x => x.Content);
    }

    /// <summary>
    /// The extension → <see cref="DocumentType"/> table matches the ticket upload (SPEC 36) so the same
    /// file uploaded to a ticket and to an activity carries the same type tag.
    /// </summary>
    [Theory]
    [InlineData(".pdf", DocumentType.Pdf)]
    [InlineData(".doc", DocumentType.Word)]
    [InlineData(".docx", DocumentType.Word)]
    [InlineData(".xls", DocumentType.Excel)]
    [InlineData(".xlsx", DocumentType.Excel)]
    [InlineData(".txt", DocumentType.Text)]
    [InlineData(".csv", DocumentType.Text)]
    [InlineData(".png", DocumentType.Image)]
    [InlineData(".jpg", DocumentType.Image)]
    [InlineData(".jpeg", DocumentType.Image)]
    [InlineData(".zip", DocumentType.Other)]
    public void TryMapExtension_MapsKnownExtensions(string extension, DocumentType expected)
    {
        UploadCalendarEventDocumentCommandHandler.TryMapExtension(extension, out var type).Should().BeTrue();
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(".gif")]
    [InlineData(".exe")]
    [InlineData("")]
    [InlineData(".PDF")]  // uppercase is normalized before this call in production
    public void TryMapExtension_RejectsUnknownExtensions(string extension)
    {
        UploadCalendarEventDocumentCommandHandler.TryMapExtension(extension, out _).Should().BeFalse();
    }
}