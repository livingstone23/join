using FluentAssertions;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.Mappings.Admin;

/// <summary>
/// Contains unit tests for <see cref="PersonMapper"/> using the real source-generated implementation.
/// </summary>
public sealed class PersonMapperTests
{
    private readonly PersonMapper _mapper = new();

    /// <summary>
    /// Verifies that all supported customer properties and nested collections are mapped from the create command.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var command = CreateValidCreatePersonCommand();

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.PersonType.Should().Be(PersonType.Legal);
        entity.FirstName.Should().Be(command.FirstName);
        entity.MiddleName.Should().Be(command.MiddleName);
        entity.LastName.Should().Be(command.LastName);
        entity.SecondLastName.Should().Be(command.SecondLastName);
        entity.CommercialName.Should().Be(command.CommercialName);
        entity.IdentificationTypeId.Should().Be(command.IdentificationTypeId);
        entity.IdentificationNumber.Should().Be(command.IdentificationNumber);

        entity.Id.Should().NotBeEmpty();
        entity.CompanyId.Should().Be(Guid.Empty);
        entity.Created.Should().NotBe(default);
        entity.CreatedBy.Should().BeNull();
        entity.LastModified.Should().BeNull();
        entity.LastModifiedBy.Should().BeNull();
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        entity.IdentificationType.Should().BeNull();
        entity.Tickets.Should().BeEmpty();

        entity.Addresses.Should().NotBeNull();
        entity.Addresses.Should().HaveCount(1);
        AssertAddressEntityMatchesCreateAddress(command.Addresses!.Single(), entity.Addresses.Single());

        entity.Contacts.Should().NotBeNull();
        entity.Contacts.Should().HaveCount(1);
        AssertContactEntityMatchesCreateContact(command.Contacts!.Single(), entity.Contacts.Single());
    }

    /// <summary>
    /// Verifies that null nested collections are materialized as empty collections when creating a customer entity.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandCollectionsAreNull_ShouldMapEmptyCollections()
    {
        // Arrange
        var command = CreateValidCreatePersonCommand() with
        {
            Addresses = null,
            Contacts = null
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Addresses.Should().NotBeNull();
        entity.Addresses.Should().BeEmpty();
        entity.Contacts.Should().NotBeNull();
        entity.Contacts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that empty nested collections remain empty when creating a customer entity.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandCollectionsAreEmpty_ShouldMapEmptyCollections()
    {
        // Arrange
        var command = CreateValidCreatePersonCommand() with
        {
            Addresses = Array.Empty<CreatePersonCommand.CreatePersonAddressDto>(),
            Contacts = Array.Empty<CreatePersonCommand.CreatePersonContactDto>()
        };

        // Act
        var entity = _mapper.ToEntity(command);

        // Assert
        entity.Addresses.Should().NotBeNull();
        entity.Addresses.Should().BeEmpty();
        entity.Contacts.Should().NotBeNull();
        entity.Contacts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a null create command throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToEntity_WhenCreateCommandIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        CreatePersonCommand command = null!;

        // Act
        Action act = () => _mapper.ToEntity(command);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that all supported properties are mapped from a create-address payload.
    /// </summary>
    [Fact]
    public void ToAddressEntity_WhenCreateAddressIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var addressDto = CreateValidCreatePersonCommand().Addresses!.Single();

        // Act
        var entity = _mapper.ToAddressEntity(addressDto);

        // Assert
        AssertAddressEntityMatchesCreateAddress(addressDto, entity);
    }

    /// <summary>
    /// Verifies that all supported properties are mapped from a create-contact payload.
    /// </summary>
    [Fact]
    public void ToContactEntity_WhenCreateContactIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var contactDto = CreateValidCreatePersonCommand().Contacts!.Single();

        // Act
        var entity = _mapper.ToContactEntity(contactDto);

        // Assert
        AssertContactEntityMatchesCreateContact(contactDto, entity);
    }

    /// <summary>
    /// Verifies that a customer DTO maps back to the entity while ignored members stay untouched.
    /// </summary>
    [Fact]
    public void ToEntity_WhenPersonDtoIsFullyPopulated_ShouldMapAllSupportedFieldsAndIgnoreNestedMembers()
    {
        // Arrange
        var dto = new PersonDto
        {
            Id = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            PersonType = nameof(PersonType.Physical),
            FirstName = "Jane",
            MiddleName = "Marie",
            LastName = "Doe",
            SecondLastName = "Smith",
            CommercialName = "Contoso",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationTypeName = "Passport",
            IdentificationNumber = "ID-001",
            Addresses =
            [
                new PersonAddressDto
                {
                    Id = Guid.NewGuid(),
                    AddressLine1 = "Ignored"
                }
            ],
            Contacts =
            [
                new PersonContactDto
                {
                    Id = Guid.NewGuid(),
                    ContactType = nameof(ContactType.PrimaryEmail),
                    ContactValue = "ignored@example.com"
                }
            ]
        };

        // Act
        var entity = _mapper.ToEntity(dto);

        // Assert
        entity.Id.Should().NotBe(dto.Id);
        entity.Id.Should().NotBeEmpty();
        entity.CompanyId.Should().Be(dto.CompanyId);
        entity.PersonType.Should().Be(PersonType.Physical);
        entity.FirstName.Should().Be(dto.FirstName);
        entity.MiddleName.Should().Be(dto.MiddleName);
        entity.LastName.Should().Be(dto.LastName);
        entity.SecondLastName.Should().Be(dto.SecondLastName);
        entity.CommercialName.Should().Be(dto.CommercialName);
        entity.IdentificationTypeId.Should().Be(dto.IdentificationTypeId);
        entity.IdentificationNumber.Should().Be(dto.IdentificationNumber);
        entity.Created.Should().NotBe(default);
        entity.Addresses.Should().BeEmpty();
        entity.Contacts.Should().BeEmpty();
        entity.IdentificationType.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null customer DTO throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToEntity_WhenPersonDtoIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        PersonDto dto = null!;

        // Act
        Action act = () => _mapper.ToEntity(dto);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that all mapped scalar and nested properties are projected from a customer entity to a DTO.
    /// </summary>
    [Fact]
    public void ToDto_WhenPersonIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var customer = CreateValidPerson();

        // Act
        var dto = _mapper.ToDto(customer);

        // Assert
        dto.Id.Should().Be(customer.Id);
        dto.CompanyId.Should().Be(customer.CompanyId);
        dto.PersonType.Should().Be(nameof(PersonType.Legal));
        dto.FirstName.Should().Be(customer.FirstName);
        dto.MiddleName.Should().Be(customer.MiddleName);
        dto.LastName.Should().Be(customer.LastName);
        dto.SecondLastName.Should().Be(customer.SecondLastName);
        dto.CommercialName.Should().Be(customer.CommercialName);
        dto.IdentificationTypeId.Should().Be(customer.IdentificationTypeId);
        dto.IdentificationTypeName.Should().Be(customer.IdentificationType.Name);
        dto.IdentificationNumber.Should().Be(customer.IdentificationNumber);

        dto.Addresses.Should().NotBeNull();
        dto.Addresses.Should().HaveCount(1);
        AssertAddressDtoMatchesEntity(customer.Addresses.Single(), dto.Addresses.Single());

        dto.Contacts.Should().NotBeNull();
        dto.Contacts.Should().HaveCount(1);
        AssertContactDtoMatchesEntity(customer.Contacts.Single(), dto.Contacts.Single());
    }

    /// <summary>
    /// Verifies that null nested collections trigger the current generated null-reference behavior when mapping a customer to its DTO.
    /// </summary>
    [Fact]
    public void ToDto_WhenPersonCollectionsAreNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        var customer = CreateValidPerson();
        customer.Addresses = null!;
        customer.Contacts = null!;

        // Act
        Action act = () => _mapper.ToDto(customer);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that empty nested collections remain empty when mapping a customer to its DTO.
    /// </summary>
    [Fact]
    public void ToDto_WhenPersonCollectionsAreEmpty_ShouldMapEmptyCollections()
    {
        // Arrange
        var customer = CreateValidPerson();
        customer.Addresses = [];
        customer.Contacts = [];

        // Act
        var dto = _mapper.ToDto(customer);

        // Assert
        dto.Addresses.Should().NotBeNull();
        dto.Addresses.Should().BeEmpty();
        dto.Contacts.Should().NotBeNull();
        dto.Contacts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a null customer source throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToDto_WhenPersonIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        Person customer = null!;

        // Act
        Action act = () => _mapper.ToDto(customer);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that an address entity maps all supported scalar and display-name properties.
    /// </summary>
    [Fact]
    public void ToAddressDto_WhenAddressIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var address = CreateValidAddress();

        // Act
        var dto = _mapper.ToAddressDto(address);

        // Assert
        AssertAddressDtoMatchesEntity(address, dto);
    }

    /// <summary>
    /// Verifies that an address without region keeps the nullable display members null.
    /// </summary>
    [Fact]
    public void ToAddressDto_WhenRegionIsNull_ShouldMapNullableRegionMembersAsNull()
    {
        // Arrange
        var address = CreateValidAddress();
        address.RegionId = null;
        address.Region = null;

        // Act
        var dto = _mapper.ToAddressDto(address);

        // Assert
        dto.RegionId.Should().BeNull();
        dto.RegionName.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a null address source throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToAddressDto_WhenAddressIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        PersonAddress address = null!;

        // Act
        Action act = () => _mapper.ToAddressDto(address);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that a contact entity maps all supported scalar properties.
    /// </summary>
    [Fact]
    public void ToContactDto_WhenContactIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var contact = CreateValidContact();

        // Act
        var dto = _mapper.ToContactDto(contact);

        // Assert
        AssertContactDtoMatchesEntity(contact, dto);
    }

    /// <summary>
    /// Verifies that a null contact source throws the current generated null-reference exception.
    /// </summary>
    [Fact]
    public void ToContactDto_WhenContactIsNull_ShouldThrowNullReferenceException()
    {
        // Arrange
        PersonContact contact = null!;

        // Act
        Action act = () => _mapper.ToContactDto(contact);

        // Assert
        act.Should().Throw<NullReferenceException>()
            .WithMessage("Object reference not set to an instance of an object.");
    }

    /// <summary>
    /// Verifies that scalar customer fields are updated while ignored members remain untouched.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenPersonCommandIsFullyPopulated_ShouldUpdateMappedFieldsOnly()
    {
        // Arrange
        var command = CreateValidUpdatePersonCommand();
        var customer = CreateValidPerson();
        var originalId = customer.Id;
        var originalCompanyId = customer.CompanyId;
        var originalAddresses = customer.Addresses;
        var originalContacts = customer.Contacts;
        var originalTickets = customer.Tickets;
        var originalCreated = customer.Created;

        // Act
        _mapper.ApplyUpdate(command, customer);

        // Assert
        customer.Id.Should().Be(originalId);
        customer.CompanyId.Should().Be(originalCompanyId);
        customer.PersonType.Should().Be(PersonType.Physical);
        customer.FirstName.Should().Be(command.FirstName);
        customer.MiddleName.Should().Be(command.MiddleName);
        customer.LastName.Should().Be(command.LastName);
        customer.SecondLastName.Should().Be(command.SecondLastName);
        customer.CommercialName.Should().Be(command.CommercialName);
        customer.IdentificationTypeId.Should().Be(command.IdentificationTypeId);
        customer.IdentificationNumber.Should().Be(command.IdentificationNumber);
        customer.Created.Should().Be(originalCreated);
        customer.Addresses.Should().BeSameAs(originalAddresses);
        customer.Contacts.Should().BeSameAs(originalContacts);
        customer.Tickets.Should().BeSameAs(originalTickets);
    }

    /// <summary>
    /// Verifies that all supported properties are mapped from an update-address payload to a new entity.
    /// </summary>
    [Fact]
    public void ToAddressEntity_WhenUpdateAddressIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var addressDto = CreateValidUpdatePersonCommand().Addresses.Single();

        // Act
        var entity = _mapper.ToAddressEntity(addressDto);

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.Id.Should().NotBe(addressDto.Id!.Value);
        entity.AddressLine1.Should().Be(addressDto.AddressLine1);
        entity.AddressLine2.Should().Be(addressDto.AddressLine2);
        entity.ZipCode.Should().Be(addressDto.ZipCode);
        entity.StreetTypeId.Should().Be(addressDto.StreetTypeId);
        entity.CountryId.Should().Be(addressDto.CountryId);
        entity.RegionId.Should().Be(addressDto.RegionId);
        entity.ProvinceId.Should().Be(addressDto.ProvinceId);
        entity.MunicipalityId.Should().Be(addressDto.MunicipalityId);
        entity.IsDefault.Should().Be(addressDto.IsDefault);
        entity.PersonId.Should().Be(Guid.Empty);
        entity.CompanyId.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// Verifies that an existing address entity receives only the mapped fields from an update payload.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenAddressUpdateIsFullyPopulated_ShouldUpdateMappedFieldsOnly()
    {
        // Arrange
        var source = CreateValidUpdatePersonCommand().Addresses.Single();
        var target = CreateValidAddress();
        var originalId = target.Id;
        var originalPersonId = target.PersonId;
        var originalCompanyId = target.CompanyId;
        var originalCreated = target.Created;
        var originalCountry = target.Country;
        var originalRegion = target.Region;
        var originalProvince = target.Province;
        var originalMunicipality = target.Municipality;
        var originalStreetType = target.StreetType;

        // Act
        _mapper.ApplyUpdate(source, target);

        // Assert
        target.Id.Should().Be(originalId);
        target.PersonId.Should().Be(originalPersonId);
        target.CompanyId.Should().Be(originalCompanyId);
        target.AddressLine1.Should().Be(source.AddressLine1);
        target.AddressLine2.Should().Be(source.AddressLine2);
        target.ZipCode.Should().Be(source.ZipCode);
        target.StreetTypeId.Should().Be(source.StreetTypeId);
        target.CountryId.Should().Be(source.CountryId);
        target.RegionId.Should().Be(source.RegionId);
        target.ProvinceId.Should().Be(source.ProvinceId);
        target.MunicipalityId.Should().Be(source.MunicipalityId);
        target.IsDefault.Should().Be(source.IsDefault);
        target.Created.Should().Be(originalCreated);
        target.Country.Should().BeSameAs(originalCountry);
        target.Region.Should().BeSameAs(originalRegion);
        target.Province.Should().BeSameAs(originalProvince);
        target.Municipality.Should().BeSameAs(originalMunicipality);
        target.StreetType.Should().BeSameAs(originalStreetType);
    }

    /// <summary>
    /// Verifies that all supported properties are mapped from an update-contact payload to a new entity.
    /// </summary>
    [Fact]
    public void ToContactEntity_WhenUpdateContactIsFullyPopulated_ShouldMapAllSupportedFields()
    {
        // Arrange
        var contactDto = CreateValidUpdatePersonCommand().Contacts.Single();

        // Act
        var entity = _mapper.ToContactEntity(contactDto);

        // Assert
        entity.Id.Should().NotBeEmpty();
        entity.Id.Should().NotBe(contactDto.Id!.Value);
        entity.ContactType.Should().Be(ContactType.WhatsApp);
        entity.ContactValue.Should().Be(contactDto.ContactValue);
        entity.IsPrimary.Should().Be(contactDto.IsPrimary);
        entity.Comments.Should().Be(contactDto.Comments);
        entity.PersonId.Should().Be(Guid.Empty);
        entity.CompanyId.Should().Be(Guid.Empty);
    }

    /// <summary>
    /// Verifies that an existing contact entity receives only the mapped fields from an update payload.
    /// </summary>
    [Fact]
    public void ApplyUpdate_WhenContactUpdateIsFullyPopulated_ShouldUpdateMappedFieldsOnly()
    {
        // Arrange
        var source = CreateValidUpdatePersonCommand().Contacts.Single();
        var target = CreateValidContact();
        var originalId = target.Id;
        var originalPersonId = target.PersonId;
        var originalCompanyId = target.CompanyId;
        var originalCreated = target.Created;

        // Act
        _mapper.ApplyUpdate(source, target);

        // Assert
        target.Id.Should().Be(originalId);
        target.PersonId.Should().Be(originalPersonId);
        target.CompanyId.Should().Be(originalCompanyId);
        target.ContactType.Should().Be(ContactType.WhatsApp);
        target.ContactValue.Should().Be(source.ContactValue);
        target.IsPrimary.Should().Be(source.IsPrimary);
        target.Comments.Should().Be(source.Comments);
        target.Created.Should().Be(originalCreated);
    }

    /// <summary>
    /// Verifies that query projection maps all supported customer fields.
    /// </summary>
    [Fact]
    public void ProjectToDto_WhenQueryContainsPersons_ShouldProjectAllSupportedFields()
    {
        // Arrange
        var customer = CreateValidPerson();
        var query = new[] { customer }.AsQueryable();

        // Act
        var projected = _mapper.ProjectToDto(query).Single();

        // Assert
        projected.Id.Should().Be(customer.Id);
        projected.CompanyId.Should().Be(customer.CompanyId);
        projected.PersonType.Should().Be(nameof(PersonType.Legal));
        projected.FirstName.Should().Be(customer.FirstName);
        projected.MiddleName.Should().Be(customer.MiddleName);
        projected.LastName.Should().Be(customer.LastName);
        projected.SecondLastName.Should().Be(customer.SecondLastName);
        projected.CommercialName.Should().Be(customer.CommercialName);
        projected.IdentificationTypeId.Should().Be(customer.IdentificationTypeId);
        projected.IdentificationTypeName.Should().Be(customer.IdentificationType.Name);
        projected.IdentificationNumber.Should().Be(customer.IdentificationNumber);
        projected.Addresses.Should().HaveCount(1);
        projected.Contacts.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifies that a null query source throws the current generated null-guard exception.
    /// </summary>
    [Fact]
    public void ProjectToDto_WhenQueryIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        IQueryable<Person> query = null!;

        // Act
        Action act = () => _mapper.ProjectToDto(query);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("source")
            .WithMessage("Value cannot be null. (Parameter 'source')");
    }

    private static CreatePersonCommand CreateValidCreatePersonCommand() => new()
    {
        PersonType = PersonType.Legal,
        FirstName = "Jane",
        MiddleName = "Marie",
        LastName = "Doe",
        SecondLastName = "Smith",
        CommercialName = "Contoso",
        IdentificationTypeId = Guid.NewGuid(),
        IdentificationNumber = "ID-001",
        Addresses =
        [
            new CreatePersonCommand.CreatePersonAddressDto
            {
                AddressLine1 = "Main Street 1",
                AddressLine2 = "Suite 100",
                ZipCode = "11001",
                StreetTypeId = Guid.NewGuid(),
                CountryId = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                ProvinceId = Guid.NewGuid(),
                MunicipalityId = Guid.NewGuid(),
                IsDefault = true
            }
        ],
        Contacts =
        [
            new CreatePersonCommand.CreatePersonContactDto
            {
                ContactType = nameof(ContactType.PrimaryEmail),
                ContactValue = "jane@example.com",
                IsPrimary = true,
                Comments = "Main email"
            }
        ]
    };

    private static UpdatePersonCommand CreateValidUpdatePersonCommand() => new()
    {
        Id = Guid.NewGuid(),
        PersonType = PersonType.Physical,
        GenderId = Guid.NewGuid(),
        IsActive = true,
        FirstName = "Updated",
        MiddleName = "Anne",
        LastName = "Person",
        SecondLastName = "Jones",
        CommercialName = "Updated Co",
        IdentificationTypeId = Guid.NewGuid(),
        IdentificationNumber = "NEW-001",
        Addresses =
        [
            new UpdatePersonCommand.UpdatePersonAddressDto
            {
                Id = Guid.NewGuid(),
                AddressLine1 = "Avenue 5",
                AddressLine2 = "Suite 3",
                ZipCode = "22002",
                StreetTypeId = Guid.NewGuid(),
                CountryId = Guid.NewGuid(),
                RegionId = Guid.NewGuid(),
                ProvinceId = Guid.NewGuid(),
                MunicipalityId = Guid.NewGuid(),
                IsDefault = false
            }
        ],
        Contacts =
        [
            new UpdatePersonCommand.UpdatePersonContactDto
            {
                Id = Guid.NewGuid(),
                ContactType = nameof(ContactType.WhatsApp),
                ContactValue = "+15550001234",
                IsPrimary = false,
                Comments = "WhatsApp channel"
            }
        ]
    };

    private static Person CreateValidPerson()
    {
        var customer = new Person
        {
            CompanyId = Guid.NewGuid(),
            PersonType = PersonType.Legal,
            FirstName = "Jane",
            MiddleName = "Marie",
            LastName = "Doe",
            SecondLastName = "Smith",
            CommercialName = "Contoso",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "ID-001",
            IdentificationType = new IdentificationType { Name = "Passport" }
        };

        SetEntityId(customer, Guid.NewGuid());

        var address = CreateValidAddress();
        var contact = CreateValidContact();
        customer.Addresses = [address];
        customer.Contacts = [contact];
        customer.Tickets = [];

        return customer;
    }

    private static PersonAddress CreateValidAddress()
    {
        var address = new PersonAddress
        {
            PersonId = Guid.NewGuid(),
            CompanyId = Guid.NewGuid(),
            AddressLine1 = "Main Street 1",
            AddressLine2 = "Suite 100",
            ZipCode = "11001",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            RegionId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid(),
            StreetType = new StreetType { Name = "Avenue" },
            Country = new Country { Name = "Nicaragua" },
            Region = new Region { Name = "Pacific" },
            Province = new Province { Name = "Managua" },
            Municipality = new Municipality { Name = "Managua" }
        };

        address.SetAsDefault();
        SetEntityId(address, Guid.NewGuid());
        return address;
    }

    private static PersonContact CreateValidContact()
    {
        var personId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var contact = PersonContact.Create(
            companyId,
            personId,
            ContactType.PrimaryEmail,
            "jane@example.com",
            "Main email");
        contact.SetAsPrimary();

        SetEntityId(contact, Guid.NewGuid());
        return contact;
    }

    private static void SetEntityId(BaseEntity entity, Guid id)
        => typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);

    private static void AssertAddressEntityMatchesCreateAddress(
        CreatePersonCommand.CreatePersonAddressDto source,
        PersonAddress target)
    {
        target.AddressLine1.Should().Be(source.AddressLine1);
        target.AddressLine2.Should().Be(source.AddressLine2);
        target.ZipCode.Should().Be(source.ZipCode);
        target.StreetTypeId.Should().Be(source.StreetTypeId);
        target.CountryId.Should().Be(source.CountryId);
        target.RegionId.Should().Be(source.RegionId);
        target.ProvinceId.Should().Be(source.ProvinceId);
        target.MunicipalityId.Should().Be(source.MunicipalityId);
        target.IsDefault.Should().Be(source.IsDefault);
        target.Id.Should().NotBeEmpty();
        target.PersonId.Should().Be(Guid.Empty);
        target.CompanyId.Should().Be(Guid.Empty);
        target.Created.Should().NotBe(default);
    }

    private static void AssertContactEntityMatchesCreateContact(
        CreatePersonCommand.CreatePersonContactDto source,
        PersonContact target)
    {
        target.ContactType.Should().Be(ContactType.PrimaryEmail);
        target.ContactValue.Should().Be(source.ContactValue);
        target.IsPrimary.Should().Be(source.IsPrimary);
        target.Comments.Should().Be(source.Comments);
        target.Id.Should().NotBeEmpty();
        target.PersonId.Should().Be(Guid.Empty);
        target.CompanyId.Should().Be(Guid.Empty);
        target.Created.Should().NotBe(default);
    }

    private static void AssertAddressDtoMatchesEntity(PersonAddress source, PersonAddressDto target)
    {
        target.Id.Should().Be(source.Id);
        target.AddressLine1.Should().Be(source.AddressLine1);
        target.AddressLine2.Should().Be(source.AddressLine2);
        target.ZipCode.Should().Be(source.ZipCode);
        target.StreetTypeId.Should().Be(source.StreetTypeId);
        target.StreetTypeName.Should().Be(source.StreetType.Name);
        target.CountryId.Should().Be(source.CountryId);
        target.CountryName.Should().Be(source.Country.Name);
        target.RegionId.Should().Be(source.RegionId);
        target.RegionName.Should().Be(source.Region?.Name);
        target.ProvinceId.Should().Be(source.ProvinceId);
        target.ProvinceName.Should().Be(source.Province.Name);
        target.MunicipalityId.Should().Be(source.MunicipalityId);
        target.MunicipalityName.Should().Be(source.Municipality.Name);
        target.IsDefault.Should().Be(source.IsDefault);
        target.CreatedAt.Should().Be(string.Empty);
    }

    private static void AssertContactDtoMatchesEntity(PersonContact source, PersonContactDto target)
    {
        target.Id.Should().Be(source.Id);
        target.ContactType.Should().Be(source.ContactType.ToString());
        target.ContactValue.Should().Be(source.ContactValue);
        target.IsPrimary.Should().Be(source.IsPrimary);
        target.Comments.Should().Be(source.Comments);
        target.CreatedAt.Should().Be(string.Empty);
    }
}
