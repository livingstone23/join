using FluentAssertions;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.TaxRegimes.Commands.Validators;

/// <summary>
/// Contains the unit tests for the tax regime command validators.
/// </summary>
public sealed class TaxRegimeCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayloadAndRejectMissingFields()
    {
        var validator = new CreateTaxRegimeCommandValidator();

        validator.Validate(new CreateTaxRegimeCommand { Code = "M", Name = "Masculino" }).IsValid.Should().BeTrue();
        validator.Validate(new CreateTaxRegimeCommand { Code = "", Name = new string('x', 151) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Code", "Name"]);
    }

    [Fact]
    public void UpdateValidator_ShouldRejectEmptyIdAndOversizedCode()
    {
        var validator = new UpdateTaxRegimeCommandValidator();

        validator.Validate(new UpdateTaxRegimeCommand { Id = Guid.NewGuid(), Code = "M", Name = "Masculino" }).IsValid.Should().BeTrue();
        validator.Validate(new UpdateTaxRegimeCommand { Id = Guid.Empty, Code = new string('x', 51), Name = "" })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Code", "Name"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        var validator = new DeleteTaxRegimeCommandValidator();

        validator.Validate(new DeleteTaxRegimeCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(new DeleteTaxRegimeCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
