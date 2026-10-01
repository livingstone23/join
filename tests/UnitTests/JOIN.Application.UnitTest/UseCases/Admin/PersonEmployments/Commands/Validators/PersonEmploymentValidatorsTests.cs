using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.Commands.Validators;

/// <summary>
/// Contains the unit tests for the person employment command validators.
/// </summary>
public sealed class PersonEmploymentValidatorsTests
{
    private static readonly DateTime Start = new(2024, 1, 1);

    [Fact]
    public void CreateValidator_ShouldAcceptCurrentEmploymentWithoutEndDate()
    {
        new CreatePersonEmploymentValidator().Validate(new CreatePersonEmploymentCommand
        {
            PersonId = Guid.NewGuid(), EmployerName = "JOIN", JobTitle = "Dev", StartDate = Start, IsCurrent = true
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateValidator_ShouldRequireEndDateWhenNotCurrent()
    {
        var result = new CreatePersonEmploymentValidator().Validate(new CreatePersonEmploymentCommand
        {
            PersonId = Guid.NewGuid(), EmployerName = "JOIN", JobTitle = "Dev", StartDate = Start, IsCurrent = false
        });

        result.Errors.Should().ContainSingle(e => e.PropertyName == "EndDate");
    }

    [Fact]
    public void CreateValidator_ShouldRejectInvalidFields()
    {
        var result = new CreatePersonEmploymentValidator().Validate(new CreatePersonEmploymentCommand
        {
            EmployerName = new string('x', 201), JobTitle = new string('x', 151), StartDate = Start, EndDate = Start.AddDays(-1)
        });

        result.Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["PersonId", "EmployerName", "JobTitle", "EndDate"]);
    }

    [Fact]
    public void UpdateValidator_ShouldAcceptAndRejectPayloads()
    {
        var validator = new UpdatePersonEmploymentValidator();

        validator.Validate(new UpdatePersonEmploymentCommand
        {
            Id = Guid.NewGuid(), PersonId = Guid.NewGuid(), EmployerName = "JOIN", JobTitle = "Dev", StartDate = Start, EndDate = Start.AddYears(1), IsCurrent = false
        }).IsValid.Should().BeTrue();

        validator.Validate(new UpdatePersonEmploymentCommand { EmployerName = "", JobTitle = "", IsCurrent = false })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "PersonId", "EmployerName", "JobTitle", "StartDate", "EndDate"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeletePersonEmploymentValidator().Validate(new DeletePersonEmploymentCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }
}
