using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Commands.DeletePerson;

/// <summary>
/// Contains the unit tests for the logical delete flow of customers.
/// The suite verifies tenant protection, not-found cases, persistence failures,
/// and the soft-delete side effects on the aggregate and its child collections.
/// </summary>
public sealed class DeletePersonCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path for a logical delete operation.
    /// This test ensures the customer, addresses, and contacts are soft-deleted
    /// and the mutation is persisted successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonExists_ShouldMarkAggregateAsDeletedAndReturnSuccess()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var customer = CreatePersonAggregate(customerId, companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync(customer);

        context.PersonsRepositoryMock
            .Setup(x => x.UpdateAsync(customer))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeletePersonCommand(customerId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person deleted successfully.");
        response.Data.Should().Be(customerId);

        customer.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        customer.Addresses.Should().OnlyContain(x => x.GcRecord > BaseAuditableEntity.ActiveGcRecord);
        customer.Contacts.Should().OnlyContain(x => x.GcRecord > BaseAuditableEntity.ActiveGcRecord);

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(customer), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This protects the delete operation from running without a valid company context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = CreateContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeletePersonCommand(_fixture.Create<Guid>()), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The X-Company-Id header is required.");

        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Person>()), Times.Never);
    }

    /// <summary>
    /// Verifies the not-found branch when the customer does not belong to the current company.
    /// This protects tenant isolation during logical delete operations.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonDoesNotExist_ShouldReturnPersonNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync((Person?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeletePersonCommand(customerId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
        response.Errors.Should().Contain("Person not found for the current company.");

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Person>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Verifies the persistence failure branch when no records are affected.
    /// This ensures the handler reports a failed delete operation correctly.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnDeleteFailedError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = CreateContext(companyId);
        var customer = CreatePersonAggregate(customerId, companyId);

        context.CompanyRepositoryMock
            .Setup(x => x.GetAsync(companyId))
            .ReturnsAsync(new Company { Name = "JOIN", TaxId = "RUC" });

        context.PersonsRepositoryMock
            .Setup(x => x.GetForUpdateAsync(customerId, companyId))
            .ReturnsAsync(customer);

        context.PersonsRepositoryMock
            .Setup(x => x.UpdateAsync(customer))
            .ReturnsAsync(true);

        context.UnitOfWorkMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new DeletePersonCommand(customerId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("DELETE_FAILED");
        response.Errors.Should().Contain("No records were affected while deleting the customer.");

        context.PersonsRepositoryMock.Verify(x => x.UpdateAsync(customer), Times.Once);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Creates a customer aggregate with active child entities.
    /// This allows the tests to verify the soft-delete behavior across the full aggregate.
    /// </summary>
    private static Person CreatePersonAggregate(Guid customerId, Guid companyId)
    {
        var customer = new Person
        {
            CompanyId = companyId,
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = Guid.NewGuid(),
            IdentificationNumber = "CUST-123",
            Addresses =
            [
                CreateCustomerAddress(companyId, customerId)
            ],
            Contacts =
            [
                CreateCustomerContact(companyId, customerId)
            ]
        };

        typeof(JOIN.Domain.Audit.BaseEntity)
            .GetProperty("Id")!
            .SetValue(customer, customerId);

        return customer;
    }

    private static PersonContact CreateCustomerContact(Guid companyId, Guid customerId)
    {
        var contact = PersonContact.Create(
            companyId,
            customerId,
            ContactType.PrimaryEmail,
            "customer@contoso.com");
        contact.SetAsPrimary();
        return contact;
    }

    private static PersonAddress CreateCustomerAddress(Guid companyId, Guid customerId)
    {
        var address = new PersonAddress
        {
            CompanyId = companyId,
            PersonId = customerId,
            AddressLine1 = "Main street",
            ZipCode = "11001",
            StreetTypeId = Guid.NewGuid(),
            CountryId = Guid.NewGuid(),
            ProvinceId = Guid.NewGuid(),
            MunicipalityId = Guid.NewGuid()
        };
        address.SetAsDefault();
        return address;
    }

    /// <summary>
    /// Creates the reusable mocked context for the customer delete tests.
    /// This mirrors the nested context pattern used across the suite.
    /// </summary>
    private static DeletePersonTestContext CreateContext(Guid companyId)
    {
        return new DeletePersonTestContext(companyId);
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
    /// Holds the reusable mocks and helper factory for the customer delete handler.
    /// </summary>
    private sealed class DeletePersonTestContext
    {
        public DeletePersonTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.SetupGet(x => x.Persons).Returns(PersonsRepositoryMock.Object);

            SetupRepository(UnitOfWorkMock, CompanyRepositoryMock);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IPersonsRepository> PersonsRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();

        public DeletePersonCommandHandler CreateHandler()
        {
            return new DeletePersonCommandHandler(
                UnitOfWorkMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
