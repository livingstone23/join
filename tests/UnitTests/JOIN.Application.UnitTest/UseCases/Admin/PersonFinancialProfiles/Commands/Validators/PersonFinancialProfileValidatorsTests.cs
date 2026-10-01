using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Commands.Validators;

/// <summary>
/// Contains the unit tests for the person financial profile command validators.
/// </summary>
public sealed class PersonFinancialProfileValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptAndRejectPayloads()
    {
        var validator = new CreatePersonFinancialProfileValidator();

        validator.Validate(new CreatePersonFinancialProfileCommand
        {
            PersonId = Guid.NewGuid(), IncomeRangeId = Guid.NewGuid(), SourceOfFunds = "Salary", DeclaredDate = DateTime.UtcNow
        }).IsValid.Should().BeTrue();

        validator.Validate(new CreatePersonFinancialProfileCommand { SourceOfFunds = new string('x', 251) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["PersonId", "IncomeRangeId", "SourceOfFunds", "DeclaredDate"]);
    }

    [Fact]
    public void UpdateValidator_ShouldAcceptAndRejectPayloads()
    {
        var validator = new UpdatePersonFinancialProfileValidator();

        validator.Validate(new UpdatePersonFinancialProfileCommand
        {
            Id = Guid.NewGuid(), PersonId = Guid.NewGuid(), IncomeRangeId = Guid.NewGuid(), SourceOfFunds = "Salary", DeclaredDate = DateTime.UtcNow
        }).IsValid.Should().BeTrue();

        validator.Validate(new UpdatePersonFinancialProfileCommand { SourceOfFunds = "" })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "PersonId", "IncomeRangeId", "SourceOfFunds", "DeclaredDate"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeletePersonFinancialProfileValidator().Validate(new DeletePersonFinancialProfileCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
