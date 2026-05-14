using FluentAssertions;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.CreateTicket;

/// <summary>
/// Contains unit tests for <see cref="CreateTicketCommandValidator"/>.
/// Each test exercises a single validation rule in isolation.
/// </summary>
public sealed class CreateTicketCommandValidatorTests
{
    private readonly CreateTicketCommandValidator _validator = new();

    // ──────────────────────────────────────────────
    //  Name rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty name triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameIsEmpty_ShouldReturnNameRequiredError()
    {
        var command = CreateValidCommand() with { Name = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.Name) &&
            x.ErrorMessage == "Ticket name is required.");
    }

    /// <summary>
    /// Verifies that a name exceeding 150 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenNameExceeds150Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { Name = new string('A', 151) };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.Name) &&
            x.ErrorMessage == "Ticket name cannot exceed 150 characters.");
    }

    // ──────────────────────────────────────────────
    //  Description rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty description triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionIsEmpty_ShouldReturnDescriptionRequiredError()
    {
        var command = CreateValidCommand() with { Description = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.Description) &&
            x.ErrorMessage == "Ticket description is required.");
    }

    /// <summary>
    /// Verifies that a description exceeding 2000 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenDescriptionExceeds2000Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { Description = new string('B', 2001) };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.Description) &&
            x.ErrorMessage == "Ticket description cannot exceed 2000 characters.");
    }

    // ──────────────────────────────────────────────
    //  Time rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a negative estimated time triggers the range error.
    /// </summary>
    [Fact]
    public void Validate_WhenEstimatedTimeIsNegative_ShouldReturnRangeError()
    {
        var command = CreateValidCommand() with { EstimatedTime = -1m };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.EstimatedTime) &&
            x.ErrorMessage == "Estimated time must be greater than or equal to zero.");
    }

    /// <summary>
    /// Verifies that a negative consumed time triggers the range error.
    /// </summary>
    [Fact]
    public void Validate_WhenConsumedTimeIsNegative_ShouldReturnRangeError()
    {
        var command = CreateValidCommand() with { ConsumedTime = -1m };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.ConsumedTime) &&
            x.ErrorMessage == "Consumed time must be greater than or equal to zero.");
    }

    /// <summary>
    /// Verifies that consumed time greater than estimated time triggers the comparison error.
    /// </summary>
    [Fact]
    public void Validate_WhenConsumedTimeExceedsEstimatedTime_ShouldReturnComparisonError()
    {
        var command = CreateValidCommand() with { EstimatedTime = 5m, ConsumedTime = 10m };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.ConsumedTime) &&
            x.ErrorMessage == "Consumed time cannot exceed estimated time.");
    }

    // ──────────────────────────────────────────────
    //  EffortPoints rule
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a negative effort points value triggers the Spanish-language error.
    /// </summary>
    [Fact]
    public void Validate_WhenEffortPointsIsNegative_ShouldReturnNegativeEffortError()
    {
        var command = CreateValidCommand() with { EffortPoints = -1m };

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCommand.EffortPoints) &&
            x.ErrorMessage == "El puntaje de esfuerzo no puede ser negativo.");
    }

    // ──────────────────────────────────────────────
    //  Required Guid rules (FK catalogues)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that each required FK Guid field returns its own required message when empty.
    /// </summary>
    [Theory]
    [InlineData(nameof(CreateTicketCommand.TicketStatusId), "Ticket status is required.")]
    [InlineData(nameof(CreateTicketCommand.TicketComplexityId), "Ticket complexity is required.")]
    [InlineData(nameof(CreateTicketCommand.TimeUnitId), "Time unit is required.")]
    [InlineData(nameof(CreateTicketCommand.ChannelId), "Channel is required.")]
    public void Validate_WhenRequiredGuidIsEmpty_ShouldReturnRequiredError(string propertyName, string expectedMessage)
    {
        var command = SetRequiredGuidProperty(CreateValidCommand(), propertyName, Guid.Empty);

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == propertyName &&
            x.ErrorMessage == expectedMessage);
    }

    // ──────────────────────────────────────────────
    //  Optional Guid rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that each optional FK Guid field returns its own invalid message when set to Guid.Empty.
    /// </summary>
    [Theory]
    [InlineData(nameof(CreateTicketCommand.AssignedToUserId), "Assigned user id is invalid.")]
    [InlineData(nameof(CreateTicketCommand.PersonId), "Person id is invalid.")]
    [InlineData(nameof(CreateTicketCommand.ProjectId), "Project id is invalid.")]
    [InlineData(nameof(CreateTicketCommand.AreaId), "Area id is invalid.")]
    [InlineData(nameof(CreateTicketCommand.PrecedentTicketId), "Precedent ticket id is invalid.")]
    public void Validate_WhenOptionalGuidIsEmpty_ShouldReturnInvalidError(string propertyName, string expectedMessage)
    {
        var command = SetOptionalGuidProperty(CreateValidCommand(), propertyName, Guid.Empty);

        var result = _validator.Validate(command);

        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == propertyName &&
            x.ErrorMessage == expectedMessage);
    }

    // ──────────────────────────────────────────────
    //  Happy path
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a fully valid command passes all validation rules without errors.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPassWithoutErrors()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────

    /// <summary>
    /// Creates a baseline valid command with all required fields populated.
    /// </summary>
    private static CreateTicketCommand CreateValidCommand() => new()
    {
        Name = "Valid ticket name",
        Description = "Valid ticket description",
        EstimatedTime = 8m,
        ConsumedTime = 2m,
        EffortPoints = 3m,
        IsVisibleToExternals = false,
        TicketStatusId = Guid.NewGuid(),
        TicketComplexityId = Guid.NewGuid(),
        TimeUnitId = Guid.NewGuid(),
        ChannelId = Guid.NewGuid(),
        AssignedToUserId = null,
        PersonId = null,
        ProjectId = null,
        AreaId = null,
        PrecedentTicketId = null
    };

    /// <summary>
    /// Returns a copy of the command with the named required Guid property set to the given value.
    /// </summary>
    private static CreateTicketCommand SetRequiredGuidProperty(CreateTicketCommand command, string propertyName, Guid value) =>
        propertyName switch
        {
            nameof(CreateTicketCommand.TicketStatusId) => command with { TicketStatusId = value },
            nameof(CreateTicketCommand.TicketComplexityId) => command with { TicketComplexityId = value },
            nameof(CreateTicketCommand.TimeUnitId) => command with { TimeUnitId = value },
            nameof(CreateTicketCommand.ChannelId) => command with { ChannelId = value },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName))
        };

    /// <summary>
    /// Returns a copy of the command with the named optional Guid? property set to the given value.
    /// </summary>
    private static CreateTicketCommand SetOptionalGuidProperty(CreateTicketCommand command, string propertyName, Guid value) =>
        propertyName switch
        {
            nameof(CreateTicketCommand.AssignedToUserId) => command with { AssignedToUserId = value },
            nameof(CreateTicketCommand.PersonId) => command with { PersonId = value },
            nameof(CreateTicketCommand.ProjectId) => command with { ProjectId = value },
            nameof(CreateTicketCommand.AreaId) => command with { AreaId = value },
            nameof(CreateTicketCommand.PrecedentTicketId) => command with { PrecedentTicketId = value },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName))
        };
}
