using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.FinishTicket;

/// <summary>
/// Unit tests for <see cref="FinishTicketCommandValidator"/>. Each test exercises a
/// single validation rule in isolation so the gate coverage is deterministic.
/// </summary>
public sealed class FinishTicketCommandValidatorTests
{
    private readonly FinishTicketCommandValidator _validator = new();

    /// <summary>
    /// Verifies that the empty-Id rule fires for both the ticket id and the target
    /// status id — the handler would otherwise short-circuit with TICKET_NOT_FOUND /
    /// INVALID_TICKET_STATUS, but the validator should refuse the request up front.
    /// </summary>
    [Theory]
    [InlineData(nameof(FinishTicketCommand.TicketId), "Ticket id is required.")]
    [InlineData(nameof(FinishTicketCommand.TicketStatusId), "Ticket status id is required.")]
    public void Validate_WhenRequiredGuidIsEmpty_ShouldReturnRequiredError(string propertyName, string expectedMessage)
    {
        var command = SetRequiredGuidProperty(CreateValidCommand(), propertyName, Guid.Empty);

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == propertyName &&
            x.ErrorMessage == expectedMessage);
    }

    /// <summary>
    /// Verifies that a <c>ResolutionSummary</c> over 500 characters fails the
    /// <c>MaximumLength(500).When(... is not null)</c> rule. The summary is recorded
    /// verbatim on the <c>Finalization</c> audit log entry, so unbounded input
    /// would bloat the audit table.
    /// </summary>
    [Fact]
    public void Validate_WhenResolutionSummaryExceeds500Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { ResolutionSummary = new string('Z', 501) };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(FinishTicketCommand.ResolutionSummary) &&
            x.ErrorMessage == "Resolution summary cannot exceed 500 characters.");
    }

    /// <summary>
    /// Verifies that a null <c>ResolutionSummary</c> is accepted — the handler
    /// substitutes the default "Ticket finalizado" string in that case.
    /// </summary>
    [Fact]
    public void Validate_WhenResolutionSummaryIsNull_ShouldPassWithoutErrors()
    {
        var command = CreateValidCommand() with { ResolutionSummary = null };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a fully valid command passes every rule without errors.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPassWithoutErrors()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    private static FinishTicketCommand CreateValidCommand() => new()
    {
        TicketId = Guid.NewGuid(),
        TicketStatusId = Guid.NewGuid(),
        ResolutionSummary = "Resolved by smoke test."
    };

    private static FinishTicketCommand SetRequiredGuidProperty(FinishTicketCommand command, string propertyName, Guid value) =>
        propertyName switch
        {
            nameof(FinishTicketCommand.TicketId) => command with { TicketId = value },
            nameof(FinishTicketCommand.TicketStatusId) => command with { TicketStatusId = value },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName))
        };
}