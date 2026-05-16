using FluentAssertions;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Commands.CreatePerson;

/// <summary>
/// Contains unit tests for <see cref="CreatePersonCommandValidator"/>.
/// Each test exercises a single validation rule in isolation.
/// </summary>
public sealed class CreatePersonCommandValidatorTests
{
    private readonly CreatePersonCommandValidator _validator = new();

    // ──────────────────────────────────────────────
    //  FirstName rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty first name triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenFirstNameIsEmpty_ShouldReturnFirstNameRequiredError()
    {
        var command = CreateValidCommand() with { FirstName = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.FirstName) &&
            x.ErrorMessage == "First name is required.");
    }

    /// <summary>
    /// Verifies that a first name exceeding 100 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenFirstNameExceeds100Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { FirstName = new string('A', 101) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.FirstName) &&
            x.ErrorMessage == "First name cannot exceed 100 characters.");
    }

    // ──────────────────────────────────────────────
    //  MiddleName rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a middle name exceeding 100 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenMiddleNameExceeds100Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { MiddleName = new string('B', 101) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.MiddleName) &&
            x.ErrorMessage == "Middle name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a whitespace-only middle name skips the max-length rule (conditional rule).
    /// </summary>
    [Fact]
    public void Validate_WhenMiddleNameIsWhitespace_ShouldNotReturnMiddleNameError()
    {
        var command = CreateValidCommand() with { MiddleName = "   " };

        var result = _validator.Validate(command);

        result.Errors.Should().NotContain(x => x.PropertyName == nameof(CreatePersonCommand.MiddleName));
    }

    // ──────────────────────────────────────────────
    //  LastName rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty last name triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenLastNameIsEmpty_ShouldReturnLastNameRequiredError()
    {
        var command = CreateValidCommand() with { LastName = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.LastName) &&
            x.ErrorMessage == "Last name is required.");
    }

    /// <summary>
    /// Verifies that a last name exceeding 100 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenLastNameExceeds100Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { LastName = new string('C', 101) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.LastName) &&
            x.ErrorMessage == "Last name cannot exceed 100 characters.");
    }

    // ──────────────────────────────────────────────
    //  SecondLastName rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a second last name exceeding 100 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenSecondLastNameExceeds100Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { SecondLastName = new string('D', 101) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.SecondLastName) &&
            x.ErrorMessage == "Second last name cannot exceed 100 characters.");
    }

    /// <summary>
    /// Verifies that a null second last name produces no validation error.
    /// </summary>
    [Fact]
    public void Validate_WhenSecondLastNameIsNull_ShouldNotReturnSecondLastNameError()
    {
        var command = CreateValidCommand() with { SecondLastName = null };

        var result = _validator.Validate(command);

        result.Errors.Should().NotContain(x => x.PropertyName == nameof(CreatePersonCommand.SecondLastName));
    }

    // ──────────────────────────────────────────────
    //  CommercialName rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a commercial name exceeding 200 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenCommercialNameExceeds200Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { CommercialName = new string('E', 201) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.CommercialName) &&
            x.ErrorMessage == "Commercial name cannot exceed 200 characters.");
    }

    // ──────────────────────────────────────────────
    //  PersonType rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty person type triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenPersonTypeIsInvalid_ShouldReturnPersonTypeError()
    {
        var command = CreateValidCommand() with { PersonType = (PersonType)99 };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.PersonType) &&
            x.ErrorMessage == "Person type must be Physical (1) or Legal (2).");
    }

    [Theory]
    [InlineData(PersonType.Physical)]
    [InlineData(PersonType.Legal)]
    public void Validate_WhenPersonTypeIsValid_ShouldNotReturnPersonTypeError(PersonType personType)
    {
        var command = CreateValidCommand() with
        {
            PersonType = personType,
            GenderId = personType == PersonType.Physical ? Guid.NewGuid() : null
        };

        var result = _validator.Validate(command);

        result.Errors.Should().NotContain(x => x.PropertyName == nameof(CreatePersonCommand.PersonType));
    }

    [Fact]
    public void Validate_WhenPhysicalPersonHasNoGenderId_ShouldReturnGenderRequiredError()
    {
        var command = CreateValidCommand() with { GenderId = null };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.GenderId) &&
            x.ErrorMessage == "Gender id is required for natural persons.");
    }

    [Fact]
    public void Validate_WhenLegalPersonHasGenderId_ShouldReturnGenderMustNotBeProvidedError()
    {
        var command = CreateValidCommand() with
        {
            PersonType = PersonType.Legal,
            GenderId = Guid.NewGuid()
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.GenderId) &&
            x.ErrorMessage == "Gender id must not be provided for legal persons.");
    }

    // ──────────────────────────────────────────────
    //  IdentificationTypeId rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty Guid for identification type triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenIdentificationTypeIdIsEmpty_ShouldReturnIdentificationTypeRequiredError()
    {
        var command = CreateValidCommand() with { IdentificationTypeId = Guid.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.IdentificationTypeId) &&
            x.ErrorMessage == "Identification type is required.");
    }

    // ──────────────────────────────────────────────
    //  IdentificationNumber rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty identification number triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenIdentificationNumberIsEmpty_ShouldReturnIdentificationNumberRequiredError()
    {
        var command = CreateValidCommand() with { IdentificationNumber = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.IdentificationNumber) &&
            x.ErrorMessage == "Identification number is required.");
    }

    /// <summary>
    /// Verifies that an identification number exceeding 50 characters triggers the max-length error.
    /// </summary>
    [Fact]
    public void Validate_WhenIdentificationNumberExceeds50Characters_ShouldReturnMaxLengthError()
    {
        var command = CreateValidCommand() with { IdentificationNumber = new string('F', 51) };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.IdentificationNumber) &&
            x.ErrorMessage == "Identification number cannot exceed 50 characters.");
    }

    // ──────────────────────────────────────────────
    //  Addresses collection rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that having two addresses marked as default triggers the single-default error.
    /// </summary>
    [Fact]
    public void Validate_WhenTwoAddressesAreMarkedAsDefault_ShouldReturnMultipleDefaultAddressError()
    {
        var command = CreateValidCommand() with
        {
            Addresses =
            [
                CreateValidAddressDto(isDefault: true),
                CreateValidAddressDto(isDefault: true)
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.Addresses) &&
            x.ErrorMessage == "Only one address can be marked as default.");
    }

    /// <summary>
    /// Verifies that a single address marked as default produces no collection error.
    /// </summary>
    [Fact]
    public void Validate_WhenOnlyOneAddressIsDefault_ShouldNotReturnAddressCollectionError()
    {
        var command = CreateValidCommand() with
        {
            Addresses = [CreateValidAddressDto(isDefault: true)]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().NotContain(x =>
            x.PropertyName == nameof(CreatePersonCommand.Addresses));
    }

    // ──────────────────────────────────────────────
    //  Contacts collection rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that having two contacts marked as primary triggers the single-primary error.
    /// </summary>
    [Fact]
    public void Validate_WhenTwoContactsAreMarkedAsPrimary_ShouldReturnMultiplePrimaryContactError()
    {
        var command = CreateValidCommand() with
        {
            Contacts =
            [
                CreateValidContactDto(isPrimary: true),
                CreateValidContactDto(isPrimary: true)
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(CreatePersonCommand.Contacts) &&
            x.ErrorMessage == "Only one contact can be marked as primary.");
    }

    // ──────────────────────────────────────────────
    //  Nested address validation rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty address line 1 in a nested DTO triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenNestedAddressLine1IsEmpty_ShouldReturnAddressLine1RequiredError()
    {
        var command = CreateValidCommand() with
        {
            Addresses =
            [
                CreateValidAddressDto() with { AddressLine1 = string.Empty }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("AddressLine1") &&
            x.ErrorMessage == "Address line 1 is required.");
    }

    /// <summary>
    /// Verifies that an empty zip code in a nested address DTO triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenNestedAddressZipCodeIsEmpty_ShouldReturnZipCodeRequiredError()
    {
        var command = CreateValidCommand() with
        {
            Addresses =
            [
                CreateValidAddressDto() with { ZipCode = string.Empty }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("ZipCode") &&
            x.ErrorMessage == "Zip code is required.");
    }

    /// <summary>
    /// Verifies that an empty StreetTypeId in a nested address DTO triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenNestedAddressStreetTypeIdIsEmpty_ShouldReturnStreetTypeRequiredError()
    {
        var command = CreateValidCommand() with
        {
            Addresses =
            [
                CreateValidAddressDto() with { StreetTypeId = Guid.Empty }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("StreetTypeId") &&
            x.ErrorMessage == "Street type is required.");
    }

    // ──────────────────────────────────────────────
    //  Nested contact validation rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an invalid contact type string in a nested DTO triggers the enum error.
    /// </summary>
    [Fact]
    public void Validate_WhenNestedContactTypeIsInvalidEnum_ShouldReturnInvalidContactTypeError()
    {
        var command = CreateValidCommand() with
        {
            Contacts =
            [
                CreateValidContactDto() with { ContactType = "InvalidType" }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("ContactType") &&
            x.ErrorMessage == "Contact type must be a valid value.");
    }

    /// <summary>
    /// Verifies that an empty contact value in a nested DTO triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenNestedContactValueIsEmpty_ShouldReturnContactValueRequiredError()
    {
        var command = CreateValidCommand() with
        {
            Contacts =
            [
                CreateValidContactDto() with { ContactValue = string.Empty }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("ContactValue") &&
            x.ErrorMessage == "Contact value is required.");
    }

    // ──────────────────────────────────────────────
    //  Happy path
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a fully valid command with no collections passes all rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsMinimallyValid_ShouldReturnNoErrors()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a fully valid command with a single address and single contact passes all rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandHasValidAddressAndContact_ShouldReturnNoErrors()
    {
        var command = CreateValidCommand() with
        {
            Addresses = [CreateValidAddressDto(isDefault: true)],
            Contacts = [CreateValidContactDto(isPrimary: true)]
        };

        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    /// <summary>Creates a minimally valid command that satisfies all rules.</summary>
    private static CreatePersonCommand CreateValidCommand() =>
        new()
        {
            PersonType = PersonType.Physical,
            GenderId = Guid.NewGuid(),
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-123456"
        };

    /// <summary>Creates a valid nested address DTO.</summary>
    private static CreatePersonCommand.CreatePersonAddressDto CreateValidAddressDto(bool isDefault = false) =>
        new()
        {
            AddressLine1 = "Main Street 123",
            ZipCode = "11001",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid(),
            IsDefault = isDefault
        };

    /// <summary>Creates a valid nested contact DTO.</summary>
    private static CreatePersonCommand.CreatePersonContactDto CreateValidContactDto(bool isPrimary = false) =>
        new()
        {
            ContactType = nameof(ContactType.PrimaryEmail),
            ContactValue = "jane@example.com",
            IsPrimary = isPrimary
        };
}
