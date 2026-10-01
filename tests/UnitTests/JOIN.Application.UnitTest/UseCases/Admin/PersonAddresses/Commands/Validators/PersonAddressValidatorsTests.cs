using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Commands.Validators;

/// <summary>
/// Contains the unit tests for the person address command validators.
/// </summary>
public sealed class PersonAddressValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayload()
    {
        new CreatePersonAddressValidator().Validate(new CreatePersonAddressCommand
        {
            PersonId = Guid.NewGuid(),
            AddressLine1 = "Main",
            AddressLine2 = "Apt",
            ZipCode = "1000",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            RegionId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid()
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateValidator_ShouldRejectEveryInvalidField()
    {
        var result = new CreatePersonAddressValidator().Validate(new CreatePersonAddressCommand
        {
            AddressLine1 = new string('x', 201),
            AddressLine2 = new string('x', 201),
            ZipCode = new string('x', 21),
            RegionId = Guid.Empty
        });

        result.Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(
            ["PersonId", "AddressLine1", "AddressLine2", "ZipCode", "StreetTypeId", "CountryId", "ProvinceId", "MunicipalityId", "RegionId"]);
    }

    [Fact]
    public void UpdateValidator_ShouldAcceptValidPayload()
    {
        new UpdatePersonAddressValidator().Validate(new UpdatePersonAddressCommand
        {
            Id = Guid.NewGuid(),
            PersonId = Guid.NewGuid(),
            AddressLine1 = "Main",
            ZipCode = "1000",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid()
        }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void UpdateValidator_ShouldRejectEveryInvalidField()
    {
        var result = new UpdatePersonAddressValidator().Validate(new UpdatePersonAddressCommand
        {
            AddressLine1 = "",
            AddressLine2 = new string('x', 201),
            ZipCode = "",
            RegionId = Guid.Empty
        });

        result.Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(
            ["Id", "PersonId", "AddressLine1", "AddressLine2", "ZipCode", "StreetTypeId", "CountryId", "ProvinceId", "MunicipalityId", "RegionId"]);
    }
}
