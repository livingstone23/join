using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Commands.UpdatePerson;

/// <summary>
/// Contains the unit tests for the customer update flow.
/// The suite focuses on the happy path and the most critical mutation branches,
/// including duplicate identification checks and collection synchronization.
/// </summary>
public sealed class UpdatePersonCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// This test ensures scalar fields are updated, child collections are synchronized,
    /// and the changes are persisted successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldUpdatePersonAndSynchronizeCollections()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);

        var existingAddressId = _fixture.Create<Guid>();
        var existingContactId = _fixture.Create<Guid>();
        var removedAddressId = _fixture.Create<Guid>();
        var removedContactId = _fixture.Create<Guid>();

        var customer = CreateExistingPerson(customerId, companyId, existingAddressId, existingContactId, removedAddressId, removedContactId);
        var request = CreateValidCommand(customerId, existingAddressId, existingContactId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync(customer);

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationExceptIdAsync(
                companyId,
                customerId,
                request.IdentificationTypeId,
                request.IdentificationNumber))
            .ReturnsAsync(false);

        SetupValidAddressReferences(context, request);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, customer))
            .Callback(() => ApplyPersonUpdate(request, customer));

        context.MapperMock
            .Setup(x => x.ToAddressEntity(It.IsAny<UpdatePersonCommand.UpdatePersonAddressDto>()))
            .Returns<UpdatePersonCommand.UpdatePersonAddressDto>(address => new PersonAddress
            {
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                ZipCode = address.ZipCode,
                StreetTypeId = address.StreetTypeId,
                CountryId = address.CountryId,
                RegionId = address.RegionId,
                ProvinceId = address.ProvinceId,
                MunicipalityId = address.MunicipalityId,
                IsDefault = address.IsDefault
            });

        context.MapperMock
            .Setup(x => x.ToContactEntity(It.IsAny<UpdatePersonCommand.UpdatePersonContactDto>()))
            .Returns<UpdatePersonCommand.UpdatePersonContactDto>(contact =>
            {
                Enum.TryParse<ContactType>(contact.ContactType, true, out var parsedType);
                return new PersonContact
                {
                    ContactType = parsedType,
                    ContactValue = contact.ContactValue,
                    IsPrimary = contact.IsPrimary,
                    Comments = contact.Comments
                };
            });

        context.MapperMock
            .Setup(x => x.ApplyUpdate(It.IsAny<UpdatePersonCommand.UpdatePersonAddressDto>(), It.IsAny<PersonAddress>()))
            .Callback<UpdatePersonCommand.UpdatePersonAddressDto, PersonAddress>((source, target) =>
            {
                target.AddressLine1 = source.AddressLine1;
                target.AddressLine2 = source.AddressLine2;
                target.ZipCode = source.ZipCode;
                target.StreetTypeId = source.StreetTypeId;
                target.CountryId = source.CountryId;
                target.RegionId = source.RegionId;
                target.ProvinceId = source.ProvinceId;
                target.MunicipalityId = source.MunicipalityId;
                target.IsDefault = source.IsDefault;
            });

        context.MapperMock
            .Setup(x => x.ApplyUpdate(It.IsAny<UpdatePersonCommand.UpdatePersonContactDto>(), It.IsAny<PersonContact>()))
            .Callback<UpdatePersonCommand.UpdatePersonContactDto, PersonContact>((source, target) =>
            {
                Enum.TryParse<ContactType>(source.ContactType, true, out var parsedType);
                target.ContactType = parsedType;
                target.ContactValue = source.ContactValue;
                target.IsPrimary = source.IsPrimary;
                target.Comments = source.Comments;
            });

        context.PersonsRepositoryMock
            .Setup(x => x.UpdateAsync(customer))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person updated successfully.");
        response.Data.Should().Be(customerId);

        customer.FirstName.Should().Be(request.FirstName);
        customer.IdentificationNumber.Should().Be(request.IdentificationNumber);

        customer.Addresses.Should().HaveCount(3);
        customer.Contacts.Should().HaveCount(3);

        customer.Addresses.Should().Contain(x => x.Id == existingAddressId && x.GcRecord == 0);
        customer.Addresses.Should().Contain(x => x.Id == removedAddressId && x.GcRecord > 0);
        customer.Addresses.Should().Contain(x => x.Id != existingAddressId && x.Id != removedAddressId && x.PersonId == customerId && x.CompanyId == companyId);

        customer.Contacts.Should().Contain(x => x.Id == existingContactId && x.GcRecord == 0);
        customer.Contacts.Should().Contain(x => x.Id == removedContactId && x.GcRecord > 0);
        customer.Contacts.Should().Contain(x => x.Id != existingContactId && x.Id != removedContactId && x.PersonId == customerId && x.CompanyId == companyId);

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(customer), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This protects the multi-tenant boundary before any repository work begins.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = CreateContext(Guid.Empty);
        var request = CreateValidCommand(_fixture.Create<Guid>(), null, null);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Person>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the customer does not belong to the current tenant.
    /// This ensures updates cannot be applied across company boundaries.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonDoesNotExist_ShouldReturnPersonNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(customerId, null, null);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync((Person?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CUSTOMER_NOT_FOUND");

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate identification validation branch.
    /// This prevents another customer from reusing the same identification pair in the same tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationAlreadyExists_ShouldReturnIdentificationInUseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var customer = CreateExistingPerson(customerId, companyId, _fixture.Create<Guid>(), _fixture.Create<Guid>(), _fixture.Create<Guid>(), _fixture.Create<Guid>());
        var request = CreateValidCommand(customerId, null, null);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync(customer);

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationExceptIdAsync(
                companyId,
                customerId,
                request.IdentificationTypeId,
                request.IdentificationNumber))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CUSTOMER_IDENTIFICATION_IN_USE");
        response.Errors.Should().Contain("Another customer already uses the same IdentificationTypeId and IdentificationNumber.");

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when the update affects no records.
    /// This ensures the handler reports a failed update operation correctly.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnUpdateFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var customer = CreateExistingPerson(customerId, companyId, _fixture.Create<Guid>(), _fixture.Create<Guid>(), _fixture.Create<Guid>(), _fixture.Create<Guid>());
        var request = CreateValidCommand(customerId, null, null);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync(customer);

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationExceptIdAsync(
                companyId,
                customerId,
                request.IdentificationTypeId,
                request.IdentificationNumber))
            .ReturnsAsync(false);

        SetupValidAddressReferences(context, request);

        context.MapperMock
            .Setup(x => x.ApplyUpdate(request, customer))
            .Callback(() => ApplyPersonUpdate(request, customer));

        context.MapperMock
            .Setup(x => x.ToAddressEntity(It.IsAny<UpdatePersonCommand.UpdatePersonAddressDto>()))
            .Returns<UpdatePersonCommand.UpdatePersonAddressDto>(address => new PersonAddress
            {
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                ZipCode = address.ZipCode,
                StreetTypeId = address.StreetTypeId,
                CountryId = address.CountryId,
                RegionId = address.RegionId,
                ProvinceId = address.ProvinceId,
                MunicipalityId = address.MunicipalityId,
                IsDefault = address.IsDefault
            });

        context.MapperMock
            .Setup(x => x.ToContactEntity(It.IsAny<UpdatePersonCommand.UpdatePersonContactDto>()))
            .Returns<UpdatePersonCommand.UpdatePersonContactDto>(contact =>
            {
                Enum.TryParse<ContactType>(contact.ContactType, true, out var parsedType);
                return new PersonContact
                {
                    ContactType = parsedType,
                    ContactValue = contact.ContactValue,
                    IsPrimary = contact.IsPrimary,
                    Comments = contact.Comments
                };
            });

        context.PersonsRepositoryMock
            .Setup(x => x.UpdateAsync(customer))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("UPDATE_FAILED");

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(customer), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid update command that can be customized for collection synchronization scenarios.
    /// </summary>
    private UpdatePersonCommand CreateValidCommand(Guid customerId, Guid? existingAddressId, Guid? existingContactId)
    {
        var updatedAddress = new UpdatePersonCommand.UpdatePersonAddressDto
        {
            Id = existingAddressId,
            AddressLine1 = "Updated main street",
            AddressLine2 = "Floor 2",
            ZipCode = "12001",
            StreetTypeId = _fixture.Create<Guid>(),
            CountryId = _fixture.Create<Guid>(),
            RegionId = _fixture.Create<Guid>(),
            ProvinceId = _fixture.Create<Guid>(),
            MunicipalityId = _fixture.Create<Guid>(),
            IsDefault = true
        };

        var newAddress = new UpdatePersonCommand.UpdatePersonAddressDto
        {
            Id = null,
            AddressLine1 = "Brand new avenue",
            AddressLine2 = null,
            ZipCode = "13001",
            StreetTypeId = _fixture.Create<Guid>(),
            CountryId = _fixture.Create<Guid>(),
            RegionId = _fixture.Create<Guid>(),
            ProvinceId = _fixture.Create<Guid>(),
            MunicipalityId = _fixture.Create<Guid>(),
            IsDefault = false
        };

        var updatedContact = new UpdatePersonCommand.UpdatePersonContactDto
        {
            Id = existingContactId,
            ContactType = nameof(ContactType.PrimaryEmail),
            ContactValue = "updated@contoso.com",
            IsPrimary = true,
            Comments = "Updated primary email"
        };

        var newContact = new UpdatePersonCommand.UpdatePersonContactDto
        {
            Id = null,
            ContactType = nameof(ContactType.WhatsApp),
            ContactValue = "+15550000001",
            IsPrimary = false,
            Comments = "New contact"
        };

        return _fixture.Build<UpdatePersonCommand>()
            .With(x => x.Id, customerId)
            .With(x => x.PersonType, nameof(PersonType.Physical))
            .With(x => x.FirstName, "Updated Jane")
            .With(x => x.MiddleName, "Maria")
            .With(x => x.LastName, "Doe")
            .With(x => x.SecondLastName, "Smith")
            .With(x => x.CommercialName, "Contoso Updated")
            .With(x => x.IdentificationTypeId, _fixture.Create<Guid>())
            .With(x => x.IdentificationNumber, "UPDATED-123")
            .With(x => x.Addresses, new[] { updatedAddress, newAddress })
            .With(x => x.Contacts, new[] { updatedContact, newContact })
            .Create();
    }

    /// <summary>
    /// Creates an existing customer aggregate with active addresses and contacts.
    /// The extra items allow the test to verify soft-delete behavior during synchronization.
    /// </summary>
    private static Person CreateExistingPerson(
        Guid customerId,
        Guid companyId,
        Guid existingAddressId,
        Guid existingContactId,
        Guid removedAddressId,
        Guid removedContactId)
    {
        var customer = new Person
        {
            CompanyId = companyId,
            FirstName = "Original",
            LastName = "Person",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "OLD-123",
            Addresses =
            [
                CreateAddress(existingAddressId, companyId, customerId, "Old address"),
                CreateAddress(removedAddressId, companyId, customerId, "Removed address")
            ],
            Contacts =
            [
                CreateContact(existingContactId, companyId, customerId, ContactType.PrimaryEmail, "old@contoso.com"),
                CreateContact(removedContactId, companyId, customerId, ContactType.WhatsApp, "+15551111111")
            ]
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(customer, customerId);

        return customer;
    }

    /// <summary>
    /// Applies scalar updates from the command to the tracked customer entity.
    /// This callback emulates the real mapper behavior while keeping the test focused on the handler.
    /// </summary>
    private static void ApplyPersonUpdate(UpdatePersonCommand request, Person customer)
    {
        Enum.TryParse<PersonType>(request.PersonType, true, out var parsedPersonType);
        customer.PersonType = parsedPersonType;
        customer.FirstName = request.FirstName;
        customer.MiddleName = request.MiddleName;
        customer.LastName = request.LastName;
        customer.SecondLastName = request.SecondLastName;
        customer.CommercialName = request.CommercialName;
        customer.IdentificationTypeId = request.IdentificationTypeId;
        customer.IdentificationNumber = request.IdentificationNumber;
    }

    /// <summary>
    /// Configures all address reference repositories to return valid catalog rows.
    /// </summary>
    private static void SetupValidAddressReferences(UpdatePersonTestContext context, UpdatePersonCommand request)
    {
        foreach (var address in request.Addresses)
        {
            context.StreetTypeRepositoryMock
                .Setup(x => x.GetAsync(address.StreetTypeId))
                .ReturnsAsync(new StreetType { Name = "Street", Abbreviation = "St" });

            context.CountryRepositoryMock
                .Setup(x => x.GetAsync(address.CountryId))
                .ReturnsAsync(new Country { Name = "Nicaragua", IsoCode = "NI" });

            context.ProvinceRepositoryMock
                .Setup(x => x.GetAsync(address.ProvinceId))
                .ReturnsAsync(new Province { Name = "Managua", Code = "MN" });

            context.MunicipalityRepositoryMock
                .Setup(x => x.GetAsync(address.MunicipalityId))
                .ReturnsAsync(new Municipality { Name = "Managua" });

            if (address.RegionId.HasValue)
            {
                context.RegionRepositoryMock
                    .Setup(x => x.GetAsync(address.RegionId.Value))
                    .ReturnsAsync(new Region { Name = "Pacific" });
            }
        }
    }

    /// <summary>
    /// Creates an existing address entity tied to the provided customer and company.
    /// </summary>
    private static PersonAddress CreateAddress(Guid id, Guid companyId, Guid customerId, string line1)
    {
        var address = new PersonAddress
        {
            CompanyId = companyId,
            PersonId = customerId,
            AddressLine1 = line1,
            ZipCode = "11001",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid(),
            IsDefault = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(address, id);

        return address;
    }

    /// <summary>
    /// Creates an existing contact entity tied to the provided customer and company.
    /// </summary>
    private static PersonContact CreateContact(Guid id, Guid companyId, Guid customerId, ContactType type, string value)
    {
        var contact = new PersonContact
        {
            CompanyId = companyId,
            PersonId = customerId,
            ContactType = type,
            ContactValue = value,
            IsPrimary = true
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(contact, id);

        return contact;
    }

    /// <summary>
    /// Creates the reusable mocked context for the customer update tests.
    /// This mirrors the nested context pattern used across the unit test suite.
    /// </summary>
    private static UpdatePersonTestContext CreateContext(Guid companyId)
    {
        return new UpdatePersonTestContext(companyId);
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise in each test.
    /// </summary>
    private static Mock<IGenericRepository<TEntity>> CreateRepositoryMock<TEntity>()
        where TEntity : class
    {
        return new Mock<IGenericRepository<TEntity>>();
    }

    /// <summary>
    /// Registers a repository in the mocked unit of work using the generic resolution pattern.
    /// </summary>
    private static void SetupRepository<TEntity>(
        Mock<IUnitOfWork> unitOfWorkMock,
        Mock<IGenericRepository<TEntity>> repositoryMock)
        where TEntity : class
    {
        unitOfWorkMock.Setup(x => x.GetRepository<TEntity>()).Returns(repositoryMock.Object);
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the customer update handler.
    /// </summary>
    private sealed class UpdatePersonTestContext
    {
        public UpdatePersonTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.SetupGet(x => x.Persons).Returns(PersonsRepositoryMock.Object);

            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
            SetupRepository(UnitOfWorkMock, IdentificationTypeRepositoryMock);
            SetupRepository(UnitOfWorkMock, StreetTypeRepositoryMock);
            SetupRepository(UnitOfWorkMock, CountryRepositoryMock);
            SetupRepository(UnitOfWorkMock, RegionRepositoryMock);
            SetupRepository(UnitOfWorkMock, ProvinceRepositoryMock);
            SetupRepository(UnitOfWorkMock, MunicipalityRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IPersonMapper> MapperMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPersonsRepository> PersonsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = CreateRepositoryMock<Company>();
        public Mock<IGenericRepository<IdentificationType>> IdentificationTypeRepositoryMock { get; } = CreateRepositoryMock<IdentificationType>();
        public Mock<IGenericRepository<StreetType>> StreetTypeRepositoryMock { get; } = CreateRepositoryMock<StreetType>();
        public Mock<IGenericRepository<Country>> CountryRepositoryMock { get; } = CreateRepositoryMock<Country>();
        public Mock<IGenericRepository<Region>> RegionRepositoryMock { get; } = CreateRepositoryMock<Region>();
        public Mock<IGenericRepository<Province>> ProvinceRepositoryMock { get; } = CreateRepositoryMock<Province>();
        public Mock<IGenericRepository<Municipality>> MunicipalityRepositoryMock { get; } = CreateRepositoryMock<Municipality>();

        public UpdatePersonCommandHandler CreateHandler()
        {
            return new UpdatePersonCommandHandler(
                UnitOfWorkMock.Object,
                MapperMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
