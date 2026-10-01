using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketComplexities.Commands.Validators;

/// <summary>
/// Contains the unit tests for the ticket complexity command validators.
/// </summary>
public sealed class TicketComplexityCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayload()
        => new CreateTicketComplexityCommandValidator()
            .Validate(new CreateTicketComplexityCommand { Name = "High", Description = "d", Code = 1, ResolutionTimeUnits = 4, TimeUnitId = Guid.NewGuid() })
            .IsValid.Should().BeTrue();

    [Fact]
    public void CreateValidator_ShouldRejectInvalidFields()
        => new CreateTicketComplexityCommandValidator()
            .Validate(new CreateTicketComplexityCommand { Name = new string('x', 51), Description = new string('x', 201) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Name", "Description", "Code", "ResolutionTimeUnits", "TimeUnitId"]);

    [Fact]
    public void UpdateValidator_ShouldAcceptValidPayload()
        => new UpdateTicketComplexityCommandValidator()
            .Validate(new UpdateTicketComplexityCommand { Id = Guid.NewGuid(), Name = "High", Code = 1, ResolutionTimeUnits = 4, TimeUnitId = Guid.NewGuid() })
            .IsValid.Should().BeTrue();

    [Fact]
    public void UpdateValidator_ShouldRejectInvalidFields()
        => new UpdateTicketComplexityCommandValidator()
            .Validate(new UpdateTicketComplexityCommand { Name = "", Description = new string('x', 201) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Name", "Description", "Code", "ResolutionTimeUnits", "TimeUnitId"]);
}
