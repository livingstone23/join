using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

/// <summary>
/// Unit tests for <see cref="UploadTicketDocumentCommandValidator"/>.
/// </summary>
public sealed class UploadTicketDocumentCommandValidatorTests
{
    private readonly UploadTicketDocumentCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenFileIsNull_ShouldReturnFileRequiredError()
    {
        var command = new UploadTicketDocumentCommand { TicketId = Guid.NewGuid(), File = null! };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(UploadTicketDocumentCommand.File) &&
            x.ErrorMessage == "File is required.");
    }

    [Fact]
    public void Validate_WhenFileNameIsEmpty_ShouldReturnFileNameRequiredError()
    {
        var command = new UploadTicketDocumentCommand
        {
            TicketId = Guid.NewGuid(),
            File = new InboundAttachment(new MemoryStream(new byte[] { 1 }), "", "application/pdf", 1)
        };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == "File.FileName" &&
            x.ErrorMessage == "File name is required.");
    }

    [Fact]
    public void Validate_WhenLengthIsZero_ShouldReturnLengthError()
    {
        var command = new UploadTicketDocumentCommand
        {
            TicketId = Guid.NewGuid(),
            File = new InboundAttachment(new MemoryStream(Array.Empty<byte>()), "report.pdf", "application/pdf", 0)
        };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == "File.Length" &&
            x.ErrorMessage == "File length must be greater than zero.");
    }

    [Fact]
    public void Validate_WhenAllFieldsValid_ShouldPassWithoutErrors()
    {
        var command = new UploadTicketDocumentCommand
        {
            TicketId = Guid.NewGuid(),
            File = new InboundAttachment(new MemoryStream(new byte[] { 1 }), "report.pdf", "application/pdf", 1)
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}