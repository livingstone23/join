using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Commands.Validators;

/// <summary>
/// Contains the unit tests for the person business profile command validators.
/// </summary>
public sealed class PersonBusinessProfileValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptAndRejectPayloads()
    {
        var validator = new CreatePersonBusinessProfileValidator();

        validator.Validate(new CreatePersonBusinessProfileCommand { PersonId = Guid.NewGuid(), IndustryId = Guid.NewGuid(), TaxRegimeId = Guid.NewGuid(), Website = "x" })
            .IsValid.Should().BeTrue();
        validator.Validate(new CreatePersonBusinessProfileCommand { Website = new string('x', 256) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["PersonId", "IndustryId", "TaxRegimeId", "Website"]);
    }

    [Fact]
    public void UpdateValidator_ShouldAcceptAndRejectPayloads()
    {
        var validator = new UpdatePersonBusinessProfileValidator();

        validator.Validate(new UpdatePersonBusinessProfileCommand { Id = Guid.NewGuid(), PersonId = Guid.NewGuid(), IndustryId = Guid.NewGuid(), TaxRegimeId = Guid.NewGuid() })
            .IsValid.Should().BeTrue();
        validator.Validate(new UpdatePersonBusinessProfileCommand { Website = new string('x', 256) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "PersonId", "IndustryId", "TaxRegimeId", "Website"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeletePersonBusinessProfileValidator().Validate(new DeletePersonBusinessProfileCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
