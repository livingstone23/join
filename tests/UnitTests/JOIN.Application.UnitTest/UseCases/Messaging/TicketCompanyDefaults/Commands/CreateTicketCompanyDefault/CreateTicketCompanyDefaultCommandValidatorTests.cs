using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketCompanyDefaults.Commands.CreateTicketCompanyDefault;

/// <summary>
/// Contains the validator coverage for ticket company default creation.
/// </summary>
public sealed class CreateTicketCompanyDefaultCommandValidatorTests
{
    private readonly CreateTicketCompanyDefaultCommandValidator _validator = new();

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
    /// Verifies the required StartCode rule.
    /// </summary>
    [Fact]
    public void Validate_WhenStartCodeIsEmpty_ShouldReturnRequiredError()
    {
        var result = _validator.Validate(CreateValidCommand() with { StartCode = string.Empty });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x =>
            x.PropertyName == nameof(CreateTicketCompanyDefaultCommand.StartCode)
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
            x.PropertyName == nameof(CreateTicketCompanyDefaultCommand.StartCode)
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
            x.PropertyName == nameof(CreateTicketCompanyDefaultCommand.CodeSequenceLength)
            && x.ErrorMessage == "CodeSequenceLength must be greater than zero.");
    }

    /// <summary>
    /// Verifies the optional Guid guards when any supplied identifier is Guid.Empty.
    /// </summary>
    [Theory]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.TicketStatusDefaultId), "The default status identifier is invalid.")]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.TicketComplexityDefaultId), "The default complexity identifier is invalid.")]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.TimeUnitDefaultId), "The default time unit identifier is invalid.")]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.AreaDefaultId), "The default area identifier is invalid.")]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.ProjectDefaultId), "The default project identifier is invalid.")]
    [InlineData(nameof(CreateTicketCompanyDefaultCommand.ChannelDefaultId), "The default communication channel identifier is invalid.")]
    public void Validate_WhenOptionalGuidIsEmpty_ShouldReturnError(string propertyName, string expectedError)
    {
        var result = _validator.Validate(SetOptionalProperty(CreateValidCommand(), propertyName, Guid.Empty));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(x => x.PropertyName == propertyName && x.ErrorMessage == expectedError);
    }

    /// <summary>
    /// Creates a valid command for reuse across validator scenarios.
    /// </summary>
    private static CreateTicketCompanyDefaultCommand CreateValidCommand()
    {
        return new CreateTicketCompanyDefaultCommand
        {
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
    private static CreateTicketCompanyDefaultCommand SetOptionalProperty(CreateTicketCompanyDefaultCommand command, string propertyName, Guid? value)
    {
        return propertyName switch
        {
            nameof(CreateTicketCompanyDefaultCommand.TicketStatusDefaultId) => command with { TicketStatusDefaultId = value },
            nameof(CreateTicketCompanyDefaultCommand.TicketComplexityDefaultId) => command with { TicketComplexityDefaultId = value },
            nameof(CreateTicketCompanyDefaultCommand.TimeUnitDefaultId) => command with { TimeUnitDefaultId = value },
            nameof(CreateTicketCompanyDefaultCommand.AreaDefaultId) => command with { AreaDefaultId = value },
            nameof(CreateTicketCompanyDefaultCommand.ProjectDefaultId) => command with { ProjectDefaultId = value },
            nameof(CreateTicketCompanyDefaultCommand.ChannelDefaultId) => command with { ChannelDefaultId = value },
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };
    }
}
