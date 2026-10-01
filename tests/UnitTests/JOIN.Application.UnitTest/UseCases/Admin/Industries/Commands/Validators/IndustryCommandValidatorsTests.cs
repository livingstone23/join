using FluentAssertions;
using JOIN.Application.UseCases.Admin.Industries.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.Industries.Commands.Validators;

/// <summary>
/// Contains the unit tests for the industry command validators.
/// </summary>
public sealed class IndustryCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldValidateRequiredAndLengthRules()
    {
        var validator = new CreateIndustryCommandValidator();

        validator.Validate(new CreateIndustryCommand { Code = "TECH", Name = "Technology", Description = "desc" }).IsValid.Should().BeTrue();
        validator.Validate(new CreateIndustryCommand { Code = "", Name = "", Description = new string('x', 501) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Code", "Name", "Description"]);
    }

    [Fact]
    public void UpdateValidator_ShouldValidateIdAndLengthRules()
    {
        var validator = new UpdateIndustryCommandValidator();

        validator.Validate(new UpdateIndustryCommand { Id = Guid.NewGuid(), Code = "TECH", Name = "Technology" }).IsValid.Should().BeTrue();
        validator.Validate(new UpdateIndustryCommand { Id = Guid.Empty, Code = new string('x', 51), Name = new string('x', 151), Description = new string('x', 501) })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Code", "Name", "Description"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeleteIndustryCommandValidator().Validate(new DeleteIndustryCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new DeleteIndustryCommandValidator().Validate(new DeleteIndustryCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
