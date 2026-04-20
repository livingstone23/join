using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketCompanyDefaults.Commands.UpdateTicketCompanyDefault;

/// <summary>
/// Contains the validator coverage for ticket company default updates.
/// </summary>
public sealed class UpdateTicketCompanyDefaultCommandValidatorTests
{
    private readonly UpdateTicketCompanyDefaultCommandValidator _validator = new();

    /// <summary>
    /// Verifies that a valid command passes all validator rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldReturnNoErrors()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the required configuration identifier rule.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldReturnRequiredError()
    {
        var result = _validator.Validate(CreateValidCommand() with { Id = Guid.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(UpdateTicketCompanyDefaultCommand.Id)
            && x.ErrorMessage == "The configuration identifier is required.");
    }

    /// <summary>
    /// Verifies the required StartCode rule.
    /// </summary>
    [Fact]
    public void Validate_WhenStartCodeIsEmpty_ShouldReturnRequiredError()
    {
        var result = _validator.Validate(CreateValidCommand() with { StartCode = string.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(UpdateTicketCompanyDefaultCommand.StartCode)
            && x.ErrorMessage == "The start code is required.");
    }

    /// <summary>
    /// Verifies the StartCode maximum-length rule.
    /// </summary>
    [Fact]
    public void Validate_WhenStartCodeExceedsTwentyCharacters_ShouldReturnMaxLengthError()
    {
        var result = _validator.Validate(CreateValidCommand() with { StartCode = new string('A', 21) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(UpdateTicketCompanyDefaultCommand.StartCode)
            && x.ErrorMessage == "The start code cannot exceed 20 characters.");
    }

    /// <summary>
    /// Verifies the positive CodeSequenceLength rule.
    /// </summary>
    [Fact]
    public void Validate_WhenCodeSequenceLengthIsNotPositive_ShouldReturnError()
    {
        var result = _validator.Validate(CreateValidCommand() with { CodeSequenceLength = 0 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(UpdateTicketCompanyDefaultCommand.CodeSequenceLength)
            && x.ErrorMessage == "CodeSequenceLength must be greater than zero.");
    }

    /// <summary>
    /// Verifies the optional Guid guards when any supplied identifier is Guid.Empty.
    /// </summary>
    [Theory]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.TicketStatusDefaultId), "The default status identifier is invalid.")]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.TicketComplexityDefaultId), "The default complexity identifier is invalid.")]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.TimeUnitDefaultId), "The default time unit identifier is invalid.")]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.AreaDefaultId), "The default area identifier is invalid.")]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.ProjectDefaultId), "The default project identifier is invalid.")]
    [InlineData(nameof(UpdateTicketCompanyDefaultCommand.ChannelDefaultId), "The default communication channel identifier is invalid.")]
    public void Validate_WhenOptionalGuidIsEmpty_ShouldReturnError(string propertyName, string expectedError)
    {
        var result = _validator.Validate(SetOptionalProperty(CreateValidCommand(), propertyName, Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.PropertyName == propertyName && x.ErrorMessage == expectedError);
    }

    /// <summary>
    /// Creates a valid command for reuse across validator scenarios.
    /// </summary>
    private static UpdateTicketCompanyDefaultCommand CreateValidCommand()
    {
        return new UpdateTicketCompanyDefaultCommand
        {
            Id = Guid.NewGuid(),
            StartCode = "JOIN",
            CodeSequenceLength = 6,
            UsePersonalizedCode = true,
            TicketStatusDefaultId = null,
            TicketComplexityDefaultId = null,
            TimeUnitDefaultId = null,
            AreaDefaultId = null,
            ProjectDefaultId = null,
            ChannelDefaultId = null
        };
    }

    /// <summary>
    /// Assigns a value to the selected optional Guid property.
    /// </summary>
    private static UpdateTicketCompanyDefaultCommand SetOptionalProperty(UpdateTicketCompanyDefaultCommand command, string propertyName, Guid? value)
    {
        return propertyName switch
        {
            nameof(UpdateTicketCompanyDefaultCommand.TicketStatusDefaultId) => command with { TicketStatusDefaultId = value },
            nameof(UpdateTicketCompanyDefaultCommand.TicketComplexityDefaultId) => command with { TicketComplexityDefaultId = value },
            nameof(UpdateTicketCompanyDefaultCommand.TimeUnitDefaultId) => command with { TimeUnitDefaultId = value },
            nameof(UpdateTicketCompanyDefaultCommand.AreaDefaultId) => command with { AreaDefaultId = value },
            nameof(UpdateTicketCompanyDefaultCommand.ProjectDefaultId) => command with { ProjectDefaultId = value },
            nameof(UpdateTicketCompanyDefaultCommand.ChannelDefaultId) => command with { ChannelDefaultId = value },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };
    }
}
