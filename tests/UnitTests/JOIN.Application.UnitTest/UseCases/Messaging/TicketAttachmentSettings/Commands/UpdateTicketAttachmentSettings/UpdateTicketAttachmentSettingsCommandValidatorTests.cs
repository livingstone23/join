using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Commands.UpdateTicketAttachmentSettings;

/// <summary>
/// Contains the unit tests for the ticket attachment settings update validator.
/// </summary>
public sealed class UpdateTicketAttachmentSettingsCommandValidatorTests
{
    private readonly UpdateTicketAttachmentSettingsCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenPayloadIsValid_ShouldPass()
        => _validator.Validate(new UpdateTicketAttachmentSettingsCommand
        {
            Id = Guid.NewGuid(),
            AllowedDocumentTypes = 0,
            MaxFileSizeBytes = 1,
            MaxFilesPerTicket = 1,
            MaxFilesPerDay = null
        }).IsValid.Should().BeTrue();

    [Fact]
    public void Validate_WhenValuesAreOutOfRange_ShouldFailEveryRule()
        => _validator.Validate(new UpdateTicketAttachmentSettingsCommand
        {
            Id = Guid.Empty,
            AllowedDocumentTypes = -4,
            MaxFileSizeBytes = -1,
            MaxFilesPerTicket = -1,
            MaxFilesPerDay = -1
        }).Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            ["Id", "AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay"]);
}
