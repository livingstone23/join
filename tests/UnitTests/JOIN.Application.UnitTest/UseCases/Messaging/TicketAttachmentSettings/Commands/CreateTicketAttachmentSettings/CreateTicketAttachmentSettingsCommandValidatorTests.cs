using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.CreateTicketAttachmentSettings;

/// <summary>
/// Contains the unit tests for the ticket attachment settings creation validator.
/// </summary>
public sealed class CreateTicketAttachmentSettingsCommandValidatorTests
{
    private readonly CreateTicketAttachmentSettingsCommandValidator _validator = new();

    [Theory]
    [InlineData(31, null)]
    [InlineData(0, 5)] // DocumentType.None is valid: the tenant accepts no attachment type yet.
    public void Validate_WhenPayloadIsValid_ShouldPass(int allowedDocumentTypes, int? maxFilesPerDay)
        => _validator.Validate(new CreateTicketAttachmentSettingsCommand
        {
            AllowedDocumentTypes = allowedDocumentTypes,
            MaxFileSizeBytes = 10_485_760,
            MaxFilesPerTicket = 10,
            MaxFilesPerDay = maxFilesPerDay
        }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_WhenValuesAreOutOfRange_ShouldFailEveryRule()
        => _validator.Validate(new CreateTicketAttachmentSettingsCommand
        {
            AllowedDocumentTypes = -1,
            MaxFileSizeBytes = 0,
            MaxFilesPerTicket = 0,
            MaxFilesPerDay = 0
        }).Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            ["AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay"]);
}
