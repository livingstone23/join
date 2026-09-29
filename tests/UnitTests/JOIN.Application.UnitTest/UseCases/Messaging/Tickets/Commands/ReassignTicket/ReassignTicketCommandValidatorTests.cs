using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.ReassignTicket;

/// <summary>
/// Unit tests for <see cref="ReassignTicketCommandValidator"/>. Each test exercises a
/// single validation rule in isolation so the gate coverage is deterministic.
/// </summary>
public sealed class ReassignTicketCommandValidatorTests
{
    private readonly ReassignTicketCommandValidator _validator = new();

    /// <summary>
    /// Verifies that an empty <see cref="ReassignTicketCommand.TicketId"/> fails the
    /// required-id rule.
    /// </summary>
    [Fact]
    public void Validate_WhenTicketIdIsEmpty_ShouldReturnTicketIdRequiredError()
    {
        var command = CreateValidCommand() with { TicketId = Guid.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(ReassignTicketCommand.TicketId) &&
            x.ErrorMessage == "Ticket id is required.");
    }

    /// <summary>
    /// Verifies that an empty <see cref="ReassignTicketCommand.NewAssignedToUserId"/>
    /// fails the required-id rule — without a destination user, the handler cannot
    /// resolve any target capability, so the validator must refuse the request up front.
    /// </summary>
    [Fact]
    public void Validate_WhenNewAssignedToUserIdIsEmpty_ShouldReturnUserIdRequiredError()
    {
        var command = CreateValidCommand() with { NewAssignedToUserId = Guid.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(ReassignTicketCommand.NewAssignedToUserId) &&
            x.ErrorMessage == "New assigned user id is required.");
    }

    private static ReassignTicketCommand CreateValidCommand() => new()
    {
        TicketId = Guid.NewGuid(),
        NewAssignedToUserId = Guid.NewGuid()
    };
}