using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.AddTicketNote;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.AddTicketNote;

/// <summary>
/// Unit tests for <see cref="AddTicketNoteCommandValidator"/>. Each test exercises a
/// single validation rule in isolation so the gate coverage is deterministic.
/// </summary>
public sealed class AddTicketNoteCommandValidatorTests
{
    private readonly AddTicketNoteCommandValidator _validator = new();

    /// <summary>
    /// Verifies that an empty <c>Summary</c> fails the required rule — the handler
    /// would otherwise reject it with INVALID_TICKET_LOG_SUMMARY inside
    /// <c>Ticket.AddLog</c>, but the validator must refuse the request up front.
    /// </summary>
    [Fact]
    public void Validate_WhenSummaryIsEmpty_ShouldReturnSummaryRequiredError()
    {
        var command = CreateValidCommand() with { Summary = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(AddTicketNoteCommand.Summary) &&
            x.ErrorMessage == "Note summary must be 1..1000 characters.");
    }

    /// <summary>
    /// Verifies that a <c>LogType</c> outside the <c>InternalNote</c> /
    /// <c>ExternalNote</c> pair fails the <c>Must</c> rule — the other LogType
    /// values are produced by the system, not by user input.
    /// </summary>
    [Fact]
    public void Validate_WhenLogTypeIsStatusChange_ShouldReturnLogTypeMustError()
    {
        var command = CreateValidCommand() with { LogType = LogType.StatusChange };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(AddTicketNoteCommand.LogType) &&
            x.ErrorMessage == "LogType must be InternalNote or ExternalNote.");
    }

    /// <summary>
    /// Verifies that a fully valid command with <c>InternalNote</c> passes every rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValidWithInternalNote_ShouldPassWithoutErrors()
    {
        var command = CreateValidCommand() with { LogType = LogType.InternalNote };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a fully valid command with <c>ExternalNote</c> passes every rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValidWithExternalNote_ShouldPassWithoutErrors()
    {
        var command = CreateValidCommand() with { LogType = LogType.ExternalNote };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    private static AddTicketNoteCommand CreateValidCommand() => new()
    {
        TicketId = Guid.NewGuid(),
        LogType = LogType.InternalNote,
        Summary = "Internal note for the test."
    };
}