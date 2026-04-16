using System.Linq;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;

namespace JOIN.UnitTests.Messaging.Tickets;

[TestClass]
public sealed class TicketCommandValidatorsTests
{
    [TestMethod]
    public void CreateValidator_ShouldAcceptValidCommand()
    {
        var validator = new CreateTicketCommandValidator();
        var command = new CreateTicketCommand
        {
            Name = "Ticket test",
            Description = "Description for validator test.",
            EstimatedTime = 8,
            ConsumedTime = 2,
            IsVisibleToExternals = true,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            AreaId = Guid.NewGuid(),
            AssignedToUserId = Guid.NewGuid(),
            PrecedentTicketId = Guid.NewGuid()
        };

        var result = validator.Validate(command);

        Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [TestMethod]
    public void CreateValidator_ShouldRejectConsumedTimeGreaterThanEstimated()
    {
        var validator = new CreateTicketCommandValidator();
        var command = new CreateTicketCommand
        {
            Name = "Ticket test",
            Description = "Description for validator test.",
            EstimatedTime = 4,
            ConsumedTime = 6,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var result = validator.Validate(command);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(CreateTicketCommand.ConsumedTime)));
    }

    [TestMethod]
    public void CreateValidator_ShouldRejectNegativeEffortPoints()
    {
        var validator = new CreateTicketCommandValidator();
        var command = new CreateTicketCommand
        {
            Name = "Ticket test",
            Description = "Description for validator test.",
            EstimatedTime = 4,
            ConsumedTime = 2,
            EffortPoints = -1,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var result = validator.Validate(command);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(CreateTicketCommand.EffortPoints)));
    }

    [TestMethod]
    public void UpdateValidator_ShouldAcceptNullEffortPoints()
    {
        var validator = new UpdateTicketCommandValidator();
        var command = new UpdateTicketCommand
        {
            Id = Guid.NewGuid(),
            Name = "Ticket test",
            Description = "Description for validator test.",
            EstimatedTime = 10,
            ConsumedTime = 1,
            EffortPoints = null,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        var result = validator.Validate(command);

        Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [TestMethod]
    public void UpdateValidator_ShouldRejectSelfReferencedPrecedent()
    {
        var validator = new UpdateTicketCommandValidator();
        var ticketId = Guid.NewGuid();

        var command = new UpdateTicketCommand
        {
            Id = ticketId,
            Name = "Ticket test",
            Description = "Description for validator test.",
            EstimatedTime = 10,
            ConsumedTime = 1,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            PrecedentTicketId = ticketId
        };

        var result = validator.Validate(command);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.ErrorMessage.Contains("cannot reference itself", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void UpdateValidator_ShouldRejectMissingRequiredIdentifiers()
    {
        var validator = new UpdateTicketCommandValidator();
        var command = new UpdateTicketCommand
        {
            Id = Guid.Empty,
            Name = string.Empty,
            Description = string.Empty,
            EstimatedTime = 0,
            ConsumedTime = 0,
            TicketStatusId = Guid.Empty,
            TicketComplexityId = Guid.Empty,
            TimeUnitId = Guid.Empty,
            ChannelId = Guid.Empty
        };

        var result = validator.Validate(command);

        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(UpdateTicketCommand.Id)));
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(UpdateTicketCommand.Name)));
        Assert.IsTrue(result.Errors.Any(e => e.PropertyName == nameof(UpdateTicketCommand.TicketStatusId)));
    }
}
