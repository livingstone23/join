using FluentAssertions;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Commands.UpdateCustomer;

/// <summary>
/// Contains unit tests for <see cref="UpdateCustomerCommandValidator"/>.
/// Each test exercises a single validation rule in isolation.
/// </summary>
public sealed class UpdateCustomerCommandValidatorTests
{
    private readonly UpdateCustomerCommandValidator _validator = new();

    // ──────────────────────────────────────────────
    //  Id rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty Guid for the customer id triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldReturnCustomerIdRequiredError()
    {
        var command = CreateValidCommand() with { Id = Guid.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(UpdateCustomerCommand.Id) &&
            x.ErrorMessage == "Customer id is required.");
    }

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
            x.PropertyName == nameof(UpdateCustomerCommand.FirstName) &&
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
            x.PropertyName == nameof(UpdateCustomerCommand.FirstName) &&
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
            x.PropertyName == nameof(UpdateCustomerCommand.MiddleName) &&
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

        result.Errors.Should().NotContain(x => x.PropertyName == nameof(UpdateCustomerCommand.MiddleName));
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
            x.PropertyName == nameof(UpdateCustomerCommand.LastName) &&
            x.ErrorMessage == "Last name is required.");
    }

    // ──────────────────────────────────────────────
    //  PersonType rules
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that an empty person type triggers the required error.
    /// </summary>
    [Fact]
    public void Validate_WhenPersonTypeIsEmpty_ShouldReturnPersonTypeRequiredError()
    {
        var command = CreateValidCommand() with { PersonType = string.Empty };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(UpdateCustomerCommand.PersonType) &&
            x.ErrorMessage == "Person type must be specified (e.g., Physical or Legal).");
    }

    /// <summary>
    /// Verifies that an unrecognized person type string triggers the invalid enum error.
    /// </summary>
    [Fact]
    public void Validate_WhenPersonTypeIsInvalidEnum_ShouldReturnInvalidPersonTypeError()
    {
        var command = CreateValidCommand() with { PersonType = "Unknown" };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName == nameof(UpdateCustomerCommand.PersonType) &&
            x.ErrorMessage == "Person type must be a valid value: Physical or Legal.");
    }

    /// <summary>
    /// Verifies that valid person type strings (case-insensitive) produce no person type error.
    /// </summary>
    [Theory]
    [InlineData("Physical")]
    [InlineData("Legal")]
    [InlineData("physical")]
    [InlineData("LEGAL")]
    public void Validate_WhenPersonTypeIsValidEnum_ShouldNotReturnPersonTypeError(string personType)
    {
        var command = CreateValidCommand() with { PersonType = personType };

        var result = _validator.Validate(command);

        result.Errors.Should().NotContain(x => x.PropertyName == nameof(UpdateCustomerCommand.PersonType));
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
            x.PropertyName == nameof(UpdateCustomerCommand.IdentificationTypeId) &&
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
            x.PropertyName == nameof(UpdateCustomerCommand.IdentificationNumber) &&
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
            x.PropertyName == nameof(UpdateCustomerCommand.IdentificationNumber) &&
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
            x.PropertyName == nameof(UpdateCustomerCommand.Addresses) &&
            x.ErrorMessage == "Only one address can be marked as default.");
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
            x.PropertyName == nameof(UpdateCustomerCommand.Contacts) &&
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
                CreateValidContactDto() with { ContactType = "BadType" }
            ]
        };

        var result = _validator.Validate(command);

        result.Errors.Should().Contain(x =>
            x.PropertyName.EndsWith("ContactType") &&
            x.ErrorMessage == "Contact type must be a valid value.");
    }

    // ──────────────────────────────────────────────
    //  Happy path
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that a fully valid command passes all rules.
    /// </summary>
    [Fact]
    public void Validate_WhenCommandIsValid_ShouldReturnNoErrors()
    {
        var result = _validator.Validate(CreateValidCommand());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    /// <summary>Creates a minimally valid command that satisfies all rules.</summary>
    private static UpdateCustomerCommand CreateValidCommand() =>
        new()
        {
            Id = Guid.NewGuid(),
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-123456"
        };

    /// <summary>Creates a valid nested address update DTO.</summary>
    private static UpdateCustomerCommand.UpdateCustomerAddressDto CreateValidAddressDto(bool isDefault = false) =>
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

    /// <summary>Creates a valid nested contact update DTO.</summary>
    private static UpdateCustomerCommand.UpdateCustomerContactDto CreateValidContactDto(bool isPrimary = false) =>
        new()
        {
            ContactType = nameof(ContactType.PrimaryEmail),
            ContactValue = "jane@example.com",
            IsPrimary = isPrimary
        };
}
