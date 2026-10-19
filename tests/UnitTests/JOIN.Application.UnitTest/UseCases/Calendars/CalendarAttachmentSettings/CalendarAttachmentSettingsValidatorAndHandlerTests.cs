using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarAttachmentSettingsTests;

/// <summary>
/// SPEC 69 — covers the two validators and the contract of the create / update commands (defaults match the
/// seed so a fresh company born without limits has the same starting line). The handlers are covered by the
/// integration tests; here we focus on the shape that flows into the API.
/// </summary>
public sealed class CalendarAttachmentSettingsValidatorAndHandlerTests
{
    /// <summary>
    /// SPEC 69 regla 7 — limits must be strictly positive; the bitmask can be <c>DocumentType.None</c> (it
    /// disables uploads by construction).
    /// </summary>
    [Fact]
    public void CreateCommandValidator_RejectsNonPositiveLimits()
    {
        var validator = new CreateCalendarAttachmentSettingsCommandValidator();
        validator.TestValidate(new CreateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 0,
            MaxFilesPerComment = 0
        }).ShouldHaveValidationErrorFor(x => x.MaxFileSizeBytes);
        validator.TestValidate(new CreateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 1,
            MaxFilesPerComment = 0
        }).ShouldHaveValidationErrorFor(x => x.MaxFilesPerComment);
    }

    /// <summary>
    /// <c>MaxFilesPerDay</c> is optional, but when provided it must be greater than zero.
    /// </summary>
    [Fact]
    public void CreateCommandValidator_AcceptsNullMaxFilesPerDay()
    {
        var validator = new CreateCalendarAttachmentSettingsCommandValidator();
        validator.TestValidate(new CreateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 1,
            MaxFilesPerComment = 1,
            MaxFilesPerDay = null
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateCommandValidator_RejectsZeroMaxFilesPerDay()
    {
        var validator = new CreateCalendarAttachmentSettingsCommandValidator();
        validator.TestValidate(new CreateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 1,
            MaxFilesPerComment = 1,
            MaxFilesPerDay = 0
        }).ShouldHaveValidationErrorFor(x => x.MaxFilesPerDay);
    }

    /// <summary>
    /// The defaults on the command are the same numbers the seed uses, so the UI can omit fields and still
    /// land on the spec starting line (regla 7).
    /// </summary>
    [Fact]
    public void CreateCommand_DefaultsMatchTheSeed()
    {
        var command = new CreateCalendarAttachmentSettingsCommand();
        command.MaxFileSizeBytes.Should().Be(10L * 1024 * 1024);
        command.MaxFilesPerComment.Should().Be(5);
        command.MaxFilesPerDay.Should().Be(200);
        command.AllowedDocumentTypes.Should().Be(
            DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image);
    }

    /// <summary>
    /// The update command requires every field — defaults are not allowed because the administrator is
    /// explicitly rewriting the row.
    /// </summary>
    [Fact]
    public void UpdateCommandValidator_RejectsZeroLimits()
    {
        var validator = new UpdateCalendarAttachmentSettingsCommandValidator();
        validator.TestValidate(new UpdateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 0,
            MaxFilesPerComment = 0
        }).ShouldHaveValidationErrorFor(x => x.MaxFileSizeBytes);
        validator.TestValidate(new UpdateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 1,
            MaxFilesPerComment = 0
        }).ShouldHaveValidationErrorFor(x => x.MaxFilesPerComment);
    }

    [Fact]
    public void UpdateCommandValidator_AcceptsNonZeroLimits()
    {
        var validator = new UpdateCalendarAttachmentSettingsCommandValidator();
        validator.TestValidate(new UpdateCalendarAttachmentSettingsCommand
        {
            MaxFileSizeBytes = 1,
            MaxFilesPerComment = 1
        }).IsValid.Should().BeTrue();
    }
}