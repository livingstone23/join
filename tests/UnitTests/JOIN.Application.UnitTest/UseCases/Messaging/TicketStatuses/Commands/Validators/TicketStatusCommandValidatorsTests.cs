using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatuses.Commands.Validators;

/// <summary>
/// Contains the unit tests for the ticket status command validators.
/// </summary>
public sealed class TicketStatusCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayload()
        => new CreateTicketStatusCommandValidator()
            .Validate(new CreateTicketStatusCommand { Name = "Open", Description = "d", Code = 1 })
            .IsValid.Should().BeTrue();

    [Fact]
    public void CreateValidator_ShouldRejectInvalidFields()
        => new CreateTicketStatusCommandValidator()
            .Validate(new CreateTicketStatusCommand { Name = "", Description = new string('x', 201), Code = 0 })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Name", "Description", "Code"]);

    [Fact]
    public void UpdateValidator_ShouldAcceptValidPayload()
        => new UpdateTicketStatusCommandValidator()
            .Validate(new UpdateTicketStatusCommand { Id = Guid.NewGuid(), Name = "Closed", Code = 9, IsFinal = true })
            .IsValid.Should().BeTrue();

    [Fact]
    public void UpdateValidator_ShouldRejectInvalidFields()
        => new UpdateTicketStatusCommandValidator()
            .Validate(new UpdateTicketStatusCommand { Name = new string('x', 51), Description = new string('x', 201), Code = -1 })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Name", "Description", "Code"]);
}
