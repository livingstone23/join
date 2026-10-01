using FluentAssertions;
using JOIN.Application.UseCases.Admin.Genders.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Commands.Validators;

/// <summary>
/// Contains the unit tests for the gender command validators.
/// </summary>
public sealed class GenderCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayloadAndRejectMissingFields()
    {
        var validator = new CreateGenderCommandValidator();

        validator.Validate(new CreateGenderCommand { Code = "M", Name = "Masculino" }).IsValid.Should().BeTrue();
        validator.Validate(new CreateGenderCommand { Code = "", Name = new string('x', 101) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Code", "Name"]);
    }

    [Fact]
    public void UpdateValidator_ShouldRejectEmptyIdAndOversizedCode()
    {
        var validator = new UpdateGenderCommandValidator();

        validator.Validate(new UpdateGenderCommand { Id = Guid.NewGuid(), Code = "M", Name = "Masculino" }).IsValid.Should().BeTrue();
        validator.Validate(new UpdateGenderCommand { Id = Guid.Empty, Code = new string('x', 21), Name = "" })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Code", "Name"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        var validator = new DeleteGenderCommandValidator();

        validator.Validate(new DeleteGenderCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(new DeleteGenderCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
