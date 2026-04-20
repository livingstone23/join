using FluentAssertions;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.Mappings.Admin;

/// <summary>
/// Contains unit tests for <see cref="CustomerMapper"/> using the real source-generated implementation.
/// Tests verify that field mappings are correct and that audit/tenant fields are intentionally excluded.
/// </summary>
public sealed class CustomerMapperTests
{
    private readonly CustomerMapper _mapper = new();

    // ──────────────────────────────────────────────
    //  ToEntity(CreateCustomerCommand)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that all scalar fields are correctly mapped from a create command to a Customer entity.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsValid_ShouldMapAllScalarFields()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            MiddleName = "Marie",
            LastName = "Doe",
            SecondLastName = "Smith",
            CommercialName = "Contoso",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-001"
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.PersonType.Should().Be(PersonType.Physical);
        entity.FirstName.Should().Be("Jane");
        entity.MiddleName.Should().Be("Marie");
        entity.LastName.Should().Be("Doe");
        entity.SecondLastName.Should().Be("Smith");
        entity.CommercialName.Should().Be("Contoso");
        entity.IdentificationTypeId.Should().Be(command.IdentificationTypeId);
        entity.IdentificationNumber.Should().Be("ID-001");
    }

    /// <summary>
    /// Verifies that the PersonType "Legal" string is correctly parsed into the <see cref="PersonType"/> enum.
    /// </summary>
    [Fact]
    public void ToEntity_WhenPersonTypeIsLegal_ShouldMapToLegalEnum()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            PersonType = nameof(PersonType.Legal),
            FirstName = "Acme Corp",
            LastName = "Inc",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "TAX-001"
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.PersonType.Should().Be(PersonType.Legal);
    }

    /// <summary>
    /// Verifies that audit and tenant fields are NOT populated by the mapper
    /// (they must be set explicitly in the handler).
    /// </summary>
    [Fact]
    public void ToEntity_WhenCommandIsMapped_ShouldLeaveAuditAndTenantFieldsAsDefault()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-001"
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.CompanyId.Should().Be(Guid.Empty);
        entity.Created.Should().NotBe(default);
        entity.CreatedBy.Should().BeNull();
        entity.LastModified.Should().BeNull();
        entity.LastModifiedBy.Should().BeNull();
    }

    /// <summary>
    /// Verifies that nested addresses in the create command are mapped to address entities.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCommandHasAddresses_ShouldMapNestedAddresses()
    {
        // Arrange
        var streetTypeId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var municipalityId = Guid.NewGuid();

        var command = new CreateCustomerCommand
        {
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-001",
            Addresses =
            [
                new CreateCustomerCommand.CreateCustomerAddressDto
                {
                    AddressLine1 = "Main Street 1",
                    ZipCode = "11001",
                    StreetTypeId = streetTypeId,
                    CountryId = countryId,
                    ProvinceId = provinceId,
                    MunicipalityId = municipalityId,
                    IsDefault = true
                }
            ]
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Addresses.Should().HaveCount(1);
        var address = entity.Addresses.First();
        address.AddressLine1.Should().Be("Main Street 1");
        address.ZipCode.Should().Be("11001");
        address.StreetTypeId.Should().Be(streetTypeId);
        address.CountryId.Should().Be(countryId);
        address.ProvinceId.Should().Be(provinceId);
        address.MunicipalityId.Should().Be(municipalityId);
        address.IsDefault.Should().BeTrue();
        address.Id.Should().NotBeEmpty();
        address.CustomerId.Should().Be(Guid.Empty);
        address.CompanyId.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// Verifies that nested contacts in the create command are mapped to contact entities.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCommandHasContacts_ShouldMapNestedContacts()
    {
        // Arrange
        var command = new CreateCustomerCommand
        {
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-001",
            Contacts =
            [
                new CreateCustomerCommand.CreateCustomerContactDto
                {
                    ContactType = nameof(ContactType.PrimaryEmail),
                    ContactValue = "jane@example.com",
                    IsPrimary = true,
                    Comments = "Main email"
                }
            ]
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Contacts.Should().HaveCount(1);
        var contact = entity.Contacts.First();
        contact.ContactType.Should().Be(ContactType.PrimaryEmail);
        contact.ContactValue.Should().Be("jane@example.com");
        contact.IsPrimary.Should().BeTrue();
        contact.Comments.Should().Be("Main email");
        contact.Id.Should().NotBeEmpty();
        contact.CustomerId.Should().Be(Guid.Empty);
        contact.CompanyId.Should().Be(Guid.Empty);
    }

    // ──────────────────────────────────────────────
    //  ApplyUpdate(UpdateCustomerCommand, Customer)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that scalar fields on the customer entity are updated from the command.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsValid_ShouldUpdateScalarFields()
    {
        // Arrange
        var newIdentificationTypeId = Guid.NewGuid();
        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            PersonType = nameof(PersonType.Legal),
            FirstName = "Updated",
            MiddleName = "Mid",
            LastName = "Customer",
            SecondLastName = "Sec",
            CommercialName = "UpdatedCo",
            IdentificationTypeId = newIdentificationTypeId,
            IdentificationNumber = "NEW-001"
        };

        var customer = new Customer
        {
            FirstName = "Original",
            LastName = "Old",
            IdentificationNumber = "OLD-001"
        };

        // Act
        _mapper.ApplyUpdate(command, customer);

        // Assert
        customer.PersonType.Should().Be(PersonType.Legal);
        customer.FirstName.Should().Be("Updated");
        customer.MiddleName.Should().Be("Mid");
        customer.LastName.Should().Be("Customer");
        customer.SecondLastName.Should().Be("Sec");
        customer.CommercialName.Should().Be("UpdatedCo");
        customer.IdentificationTypeId.Should().Be(newIdentificationTypeId);
        customer.IdentificationNumber.Should().Be("NEW-001");
    }

    /// <summary>
    /// Verifies that the customer Id, CompanyId, Addresses, and Contacts are NOT modified by ApplyUpdate.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenCommandIsApplied_ShouldNotModifyIdOrTenantOrCollections()
    {
        // Arrange
        var originalId = Guid.NewGuid();
        var originalCompanyId = Guid.NewGuid();
        var originalAddresses = new List<CustomerAddress> { new() { AddressLine1 = "Untouched" } };
        var originalContacts = new List<CustomerContact> { new() { ContactValue = "original@test.com" } };

        var command = new UpdateCustomerCommand
        {
            Id = Guid.NewGuid(),
            PersonType = nameof(PersonType.Physical),
            FirstName = "X",
            LastName = "Y",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "X-001",
            Addresses = [new UpdateCustomerCommand.UpdateCustomerAddressDto
            {
                AddressLine1 = "Should not affect entity", ZipCode = "0", IsDefault = false,
                StreetTypeId = Guid.NewGuid(), CountryId = Guid.NewGuid(),
                ProvinceId = Guid.NewGuid(), MunicipalityId = Guid.NewGuid()
            }]
        };

        var customer = new Customer
        {
            FirstName = "Original",
            LastName = "Original",
            IdentificationNumber = "OLD",
            Addresses = originalAddresses,
            Contacts = originalContacts
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(customer, originalId);

        customer.CompanyId = originalCompanyId;

        // Act
        _mapper.ApplyUpdate(command, customer);

        // Assert
        customer.Id.Should().Be(originalId);
        customer.CompanyId.Should().Be(originalCompanyId);
        customer.Addresses.Should().BeSameAs(originalAddresses);
        customer.Contacts.Should().BeSameAs(originalContacts);
    }

    // ──────────────────────────────────────────────
    //  ToAddressEntity(UpdateCustomerCommand.UpdateCustomerAddressDto)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that all address fields are correctly mapped from an update address DTO.
    /// </summary>
    [Fact]
    public void ToAddressEntity_WhenUpdateAddressDtoIsValid_ShouldMapAllFields()
    {
        // Arrange
        var streetTypeId = Guid.NewGuid();
        var countryId = Guid.NewGuid();
        var regionId = Guid.NewGuid();
        var provinceId = Guid.NewGuid();
        var municipalityId = Guid.NewGuid();

        var dto = new UpdateCustomerCommand.UpdateCustomerAddressDto
        {
            Id = Guid.NewGuid(),
            AddressLine1 = "Avenue 5",
            AddressLine2 = "Suite 3",
            ZipCode = "22002",
            StreetTypeId = streetTypeId,
            CountryId = countryId,
            RegionId = regionId,
            ProvinceId = provinceId,
            MunicipalityId = municipalityId,
            IsDefault = true
        };

        // Act
        var entity = _mapper.ToAddressEntity(dto);

        // Assert
        entity.AddressLine1.Should().Be("Avenue 5");
        entity.AddressLine2.Should().Be("Suite 3");
        entity.ZipCode.Should().Be("22002");
        entity.StreetTypeId.Should().Be(streetTypeId);
        entity.CountryId.Should().Be(countryId);
        entity.RegionId.Should().Be(regionId);
        entity.ProvinceId.Should().Be(provinceId);
        entity.MunicipalityId.Should().Be(municipalityId);
        entity.IsDefault.Should().BeTrue();
        entity.Id.Should().NotBeEmpty();
        entity.Id.Should().NotBe(dto.Id!.Value);
        entity.CustomerId.Should().Be(Guid.Empty);
        entity.CompanyId.Should().Be(Guid.Empty);
    }

    // ──────────────────────────────────────────────
    //  ToContactEntity(UpdateCustomerCommand.UpdateCustomerContactDto)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that the contact fields are mapped and the ContactType string is parsed to the enum.
    /// </summary>
    [Fact]
    public void ToContactEntity_WhenUpdateContactDtoIsValid_ShouldMapAllFieldsAndParseContactTypeEnum()
    {
        // Arrange
        var dto = new UpdateCustomerCommand.UpdateCustomerContactDto
        {
            Id = Guid.NewGuid(),
            ContactType = nameof(ContactType.WhatsApp),
            ContactValue = "+15550001234",
            IsPrimary = false,
            Comments = "WhatsApp channel"
        };

        // Act
        var entity = _mapper.ToContactEntity(dto);

        // Assert
        entity.ContactType.Should().Be(ContactType.WhatsApp);
        entity.ContactValue.Should().Be("+15550001234");
        entity.IsPrimary.Should().BeFalse();
        entity.Comments.Should().Be("WhatsApp channel");
        entity.Id.Should().NotBeEmpty();
        entity.Id.Should().NotBe(dto.Id!.Value);
        entity.CustomerId.Should().Be(Guid.Empty);
        entity.CompanyId.Should().Be(Guid.Empty);
    }

    // ──────────────────────────────────────────────
    //  ToDto(Customer)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Verifies that the mapper correctly projects a Customer entity to a CustomerDto.
    /// </summary>
    [Fact]
    public void ToDto_WhenCustomerEntityIsValid_ShouldMapAllScalarFields()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var identificationTypeId = Guid.NewGuid();

        var customer = new Customer
        {
            CompanyId = companyId,
            PersonType = PersonType.Physical,
            FirstName = "Jane",
            MiddleName = "M",
            LastName = "Doe",
            SecondLastName = "Smith",
            CommercialName = "Contoso",
            IdentificationTypeId = identificationTypeId,
            IdentificationNumber = "ID-999",
            IdentificationType = new IdentificationType { Name = "Passport" }
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(customer, customerId);

        // Act
        var dto = _mapper.ToDto(customer);

        // Assert
        dto.Id.Should().Be(customerId);
        dto.CompanyId.Should().Be(companyId);
        dto.PersonType.Should().Be(nameof(PersonType.Physical));
        dto.FirstName.Should().Be("Jane");
        dto.MiddleName.Should().Be("M");
        dto.LastName.Should().Be("Doe");
        dto.SecondLastName.Should().Be("Smith");
        dto.CommercialName.Should().Be("Contoso");
        dto.IdentificationTypeId.Should().Be(identificationTypeId);
        dto.IdentificationTypeName.Should().Be("Passport");
        dto.IdentificationNumber.Should().Be("ID-999");
    }
}
