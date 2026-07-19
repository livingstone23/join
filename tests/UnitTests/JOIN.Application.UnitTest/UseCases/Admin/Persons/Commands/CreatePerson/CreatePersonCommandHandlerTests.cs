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

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Commands.CreatePerson;

/// <summary>
/// Contains the unit tests for the customer creation flow.
/// The suite covers the happy path and the most critical guard clauses
/// to maximize meaningful branch coverage of the handler.
/// </summary>
public sealed class CreatePersonCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the request is valid.
    /// This test ensures the customer is created successfully and that
    /// nested addresses and contacts inherit the tenant and customer identifiers.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreatePersonAndPopulateChildTenantData()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var request = CreateValidCommand(includeAddresses: true, includeContacts: true);
        var customerEntity = CreateMappedPerson(request);

        var context = CreateContext(companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.GenderRepositoryMock
            .Setup(x => x.GetAsync(request.GenderId!.Value))
            .ReturnsAsync(Gender.Create(companyId, "M", "Masculino"));

        SetupValidAddressReferences(context, request);

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationAsync(companyId, request.IdentificationNumber))
            .ReturnsAsync(false);

        context.MapperMock
            .Setup(x => x.ToEntity(request))
            .Returns(customerEntity);

        context.PersonsRepositoryMock
            .Setup(x => x.InsertAsync(customerEntity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person created successfully.");
        response.Data.Should().Be(customerEntity.Id);

        customerEntity.CompanyId.Should().Be(companyId);
        customerEntity.Addresses.Should().NotBeEmpty();
        customerEntity.Contacts.Should().NotBeEmpty();
        customerEntity.Addresses.Should().OnlyContain(x =>
            x.CompanyId == companyId && x.PersonId == customerEntity.Id);
        customerEntity.Contacts.Should().OnlyContain(x =>
            x.CompanyId == companyId && x.PersonId == customerEntity.Id);

        context.MapperMock.Verify(x => x.ToEntity(request), Times.Once);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(customerEntity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This protects the multi-tenant boundary before any repository access occurs.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = CreateContext(Guid.Empty);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: false);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CompanyId header or claim is required.");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the early exit when the company does not exist.
    /// This prevents customer creation for an invalid or inactive tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: false);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync((Company?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("The provided company does not exist or is inactive.");
        response.Errors.Should().Contain("Invalid CompanyId.");

        context.MapperMock.Verify(x => x.ToEntity(It.IsAny<CreatePersonCommand>()), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the early exit when the identification type is invalid.
    /// This protects the foreign key validation branch before mapping and persistence.
    /// </summary>
    [Fact]
    public async Task Handle_WhenIdentificationTypeDoesNotExist_ShouldReturnInvalidIdentificationTypeError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: false);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync((IdentificationType?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("The provided identification type does not exist or is inactive.");
        response.Errors.Should().Contain("Invalid IdentificationTypeId.");

        context.MapperMock.Verify(x => x.ToEntity(It.IsAny<CreatePersonCommand>()), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the address reference validation branch.
    /// This test ensures the handler aggregates invalid reference errors
    /// and stops before the duplicate check and persistence steps.
    /// </summary>
    [Fact]
    public async Task Handle_WhenAddressReferencesAreInvalid_ShouldReturnAggregatedAddressErrors()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: true, includeContacts: false);
        var address = request.Addresses!.Single();

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.StreetTypeRepositoryMock
            .Setup(x => x.GetAsync(address.StreetTypeId))
            .ReturnsAsync((StreetType?)null);

        context.CountryRepositoryMock
            .Setup(x => x.GetAsync(address.CountryId))
            .ReturnsAsync((Country?)null);

        context.ProvinceRepositoryMock
            .Setup(x => x.GetAsync(address.ProvinceId))
            .ReturnsAsync((Province?)null);

        context.MunicipalityRepositoryMock
            .Setup(x => x.GetAsync(address.MunicipalityId))
            .ReturnsAsync((Municipality?)null);

        if (address.RegionId.HasValue)
        {
            context.RegionRepositoryMock
                .Setup(x => x.GetAsync(address.RegionId.Value))
                .ReturnsAsync((Region?)null);
        }

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("One or more address references are invalid.");
        response.Errors.Should().Contain(x => x.Contains("Invalid StreetTypeId"));
        response.Errors.Should().Contain(x => x.Contains("Invalid CountryId"));
        response.Errors.Should().Contain(x => x.Contains("Invalid ProvinceId"));
        response.Errors.Should().Contain(x => x.Contains("Invalid MunicipalityId"));
        response.Errors.Should().Contain(x => x.Contains("Invalid RegionId"));

        context.PersonsRepositoryMock.Verify(
            x => x.ExistsByCompanyAndIdentificationAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the contact type validation branch.
    /// This ensures invalid contact category values are rejected before persistence.
    /// </summary>
    [Fact]
    public async Task Handle_WhenContactTypesAreInvalid_ShouldReturnInvalidContactTypeError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: true, invalidContactType: true);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("One or more contact types are invalid.");
        response.Errors.Should().Contain(x => x.Contains("Invalid ContactType in contacts section"));

        context.PersonsRepositoryMock.Verify(
            x => x.ExistsByCompanyAndIdentificationAsync(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Never);
        context.MapperMock.Verify(x => x.ToEntity(It.IsAny<CreatePersonCommand>()), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the duplicate identification validation branch.
    /// This ensures an existing customer with the same identification number
    /// is rejected before mapping and insertion.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonAlreadyExists_ShouldReturnPersonAlreadyExistsError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: false);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationAsync(companyId, request.IdentificationNumber))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CUSTOMER_ALREADY_EXISTS");
        response.Errors.Should().Contain("A customer with the same identification number already exists for this company.");

        context.MapperMock.Verify(x => x.ToEntity(It.IsAny<CreatePersonCommand>()), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when the save operation affects no records.
    /// This ensures the handler reports the database failure correctly.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveAsyncReturnsZero_ShouldReturnDatabaseError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var request = CreateValidCommand(includeAddresses: false, includeContacts: false);
        var customerEntity = CreateMappedPerson(request);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.IdentificationTypeRepositoryMock
            .Setup(x => x.GetAsync(request.IdentificationTypeId))
            .ReturnsAsync(new IdentificationType { Name = "Passport" });

        context.PersonsRepositoryMock
            .Setup(x => x.ExistsByCompanyAndIdentificationAsync(companyId, request.IdentificationNumber))
            .ReturnsAsync(false);

        context.MapperMock
            .Setup(x => x.ToEntity(request))
            .Returns(customerEntity);

        context.PersonsRepositoryMock
            .Setup(x => x.InsertAsync(customerEntity))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Failed to create the customer due to a database error.");

        context.MapperMock.Verify(x => x.ToEntity(request), Times.Once);
        context.PersonsRepositoryMock.Verify(x => x.InsertAsync(customerEntity), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a valid command payload that can be customized per scenario.
    /// This keeps the arrange section focused on the branch under test.
    /// </summary>
    private CreatePersonCommand CreateValidCommand(
        bool includeAddresses = true,
        bool includeContacts = true,
        bool invalidContactType = false)
    {
        var addresses = includeAddresses
            ? new[]
            {
                new CreatePersonCommand.CreatePersonAddressDto
                {
                    AddressLine1 = "Main Street 123",
                    AddressLine2 = "Suite 5",
                    ZipCode = "11001",
                    StreetTypeId = _fixture.Create<Guid>(),
                    CountryId = _fixture.Create<Guid>(),
                    RegionId = _fixture.Create<Guid>(),
                    ProvinceId = _fixture.Create<Guid>(),
                    MunicipalityId = _fixture.Create<Guid>(),
                    IsDefault = true
                }
            }
            : Array.Empty<CreatePersonCommand.CreatePersonAddressDto>();

        var contacts = includeContacts
            ? new[]
            {
                new CreatePersonCommand.CreatePersonContactDto
                {
                    ContactType = invalidContactType ? "NotAValidType" : nameof(ContactType.PrimaryEmail),
                    ContactValue = "primary@contoso.com",
                    IsPrimary = true,
                    Comments = "Primary email"
                },
                new CreatePersonCommand.CreatePersonContactDto
                {
                    ContactType = nameof(ContactType.WhatsApp),
                    ContactValue = "+15551234567",
                    IsPrimary = false,
                    Comments = "Support line"
                }
            }
            : Array.Empty<CreatePersonCommand.CreatePersonContactDto>();

        return _fixture.Build<CreatePersonCommand>()
            .With(x => x.PersonType, PersonType.Physical)
            .With(x => x.GenderId, _fixture.Create<Guid>())
            .With(x => x.FirstName, "Jane")
            .With(x => x.MiddleName, "Maria")
            .With(x => x.LastName, "Doe")
            .With(x => x.SecondLastName, "Smith")
            .With(x => x.CommercialName, "Contoso")
            .With(x => x.IdentificationTypeId, _fixture.Create<Guid>())
            .With(x => x.IdentificationNumber, "ID-123456")
            .With(x => x.Addresses, addresses)
            .With(x => x.Contacts, contacts)
            .Create();
    }

    /// <summary>
    /// Creates the mapped customer aggregate returned by the mocked mapper.
    /// This keeps the tests focused on the handler rather than on the mapping engine.
    /// </summary>
    private static Person CreateMappedPerson(CreatePersonCommand request)
    {
        return new Person
        {
            PersonType = request.PersonType,
            GenderId = request.GenderId,
            FirstName = request.FirstName,
            MiddleName = request.MiddleName,
            LastName = request.LastName,
            SecondLastName = request.SecondLastName,
            CommercialName = request.CommercialName,
            IdentificationTypeId = request.IdentificationTypeId,
            IdentificationNumber = request.IdentificationNumber,
            Addresses = request.Addresses?.Select(address =>
            {
                var personAddress = new PersonAddress
                {
                    AddressLine1 = address.AddressLine1,
                    AddressLine2 = address.AddressLine2,
                    ZipCode = address.ZipCode,
                    StreetTypeId = address.StreetTypeId,
                    CountryId = address.CountryId,
                    RegionId = address.RegionId,
                    ProvinceId = address.ProvinceId,
                    MunicipalityId = address.MunicipalityId
                };
                if (address.IsDefault)
                {
                    personAddress.SetAsDefault();
                }
                return personAddress;
            }).ToList() ?? new List<PersonAddress>(),
            Contacts = request.Contacts?.Select(contact =>
            {
                Enum.TryParse<ContactType>(contact.ContactType, true, out var parsedContactType);
                var personContact = PersonContact.Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    parsedContactType,
                    contact.ContactValue,
                    contact.Comments);
                if (contact.IsPrimary)
                {
                    personContact.SetAsPrimary();
                }
                return personContact;
            }).ToList() ?? new List<PersonContact>()
        };
    }

    /// <summary>
    /// Configures all address-related catalog repositories to return valid references.
    /// This supports the happy-path branch that validates nested address data.
    /// </summary>
    private static void SetupValidAddressReferences(CreatePersonTestContext context, CreatePersonCommand request)
    {
        if (request.Addresses is null || request.Addresses.Count == 0)
        {
            return;
        }

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
    /// Creates the reusable mocked context for the customer creation tests.
    /// This mirrors the same nested test context pattern used by the ticket handler suites.
    /// </summary>
    private static CreatePersonTestContext CreateContext(Guid companyId)
    {
        return new CreatePersonTestContext(companyId);
    }

    /// <summary>
    /// Creates a generic repository mock to reduce arrange noise in each scenario.
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
    /// Holds the reusable mocks and helper factory for the customer creation handler.
    /// </summary>
    private sealed class CreatePersonTestContext
    {
        public CreatePersonTestContext(Guid companyId)
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
            SetupRepository(UnitOfWorkMock, GenderRepositoryMock);
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
        public Mock<IGenericRepository<Gender>> GenderRepositoryMock { get; } = CreateRepositoryMock<Gender>();

        public CreatePersonCommandHandler CreateHandler()
        {
            return new CreatePersonCommandHandler(
                UnitOfWorkMock.Object,
                MapperMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
