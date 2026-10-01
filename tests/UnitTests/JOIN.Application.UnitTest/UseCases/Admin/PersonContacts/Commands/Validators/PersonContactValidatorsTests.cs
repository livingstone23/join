using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonContacts.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.Commands.Validators;

/// <summary>
/// Contains the unit tests for the person contact command validators.
/// </summary>
public sealed class PersonContactValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidAndRejectInvalidPayloads()
    {
        var validator = new CreatePersonContactValidator();

        validator.Validate(new CreatePersonContactCommand { PersonId = Guid.NewGuid(), ContactType = ContactType.MobilePhone, ContactValue = "6000", Comments = "x" })
            .IsValid.Should().BeTrue();
        validator.Validate(new CreatePersonContactCommand { ContactType = (ContactType)0, ContactValue = new string('x', 201), Comments = new string('x', 501) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["PersonId", "ContactType", "ContactValue", "Comments"]);
    }

    [Fact]
    public void UpdateValidator_ShouldAcceptValidAndRejectInvalidPayloads()
    {
        var validator = new UpdatePersonContactValidator();

        validator.Validate(new UpdatePersonContactCommand { Id = Guid.NewGuid(), PersonId = Guid.NewGuid(), ContactType = ContactType.PrimaryEmail, ContactValue = "a@b.c" })
            .IsValid.Should().BeTrue();
        validator.Validate(new UpdatePersonContactCommand { ContactType = (ContactType)0, ContactValue = "", Comments = new string('x', 501) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "PersonId", "ContactType", "ContactValue", "Comments"]);
    }
}
