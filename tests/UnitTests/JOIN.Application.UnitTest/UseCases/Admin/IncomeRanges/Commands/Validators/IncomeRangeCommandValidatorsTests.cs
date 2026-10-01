using FluentAssertions;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Commands.Validators;

/// <summary>
/// Contains the unit tests for the income range command validators.
/// </summary>
public sealed class IncomeRangeCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldValidateRangeAndRequiredFields()
    {
        var validator = new CreateIncomeRangeCommandValidator();

        validator.Validate(new CreateIncomeRangeCommand { DisplayName = "Low", CurrencyCode = "USD", DisplayOrder = 1, MinimumValue = 0, MaximumValue = null })
            .IsValid.Should().BeTrue();
        validator.Validate(new CreateIncomeRangeCommand { DisplayName = "", CurrencyCode = "USDX", DisplayOrder = 0, MinimumValue = 10, MaximumValue = 5 })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["DisplayName", "DisplayOrder", "CurrencyCode", "MaximumValue"]);
    }

    [Fact]
    public void UpdateValidator_ShouldValidateIdRangeAndRequiredFields()
    {
        var validator = new UpdateIncomeRangeCommandValidator();

        validator.Validate(new UpdateIncomeRangeCommand { Id = Guid.NewGuid(), DisplayName = "Low", CurrencyCode = "USD", DisplayOrder = 1, MaximumValue = 10 })
            .IsValid.Should().BeTrue();
        validator.Validate(new UpdateIncomeRangeCommand { Id = Guid.Empty, DisplayName = "", CurrencyCode = "", DisplayOrder = 0, MinimumValue = 10, MaximumValue = 5 })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "DisplayName", "DisplayOrder", "CurrencyCode", "MaximumValue"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeleteIncomeRangeCommandValidator().Validate(new DeleteIncomeRangeCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
