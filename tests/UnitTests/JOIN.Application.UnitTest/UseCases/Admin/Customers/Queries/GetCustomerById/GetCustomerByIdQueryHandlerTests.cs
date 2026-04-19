using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Customers.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Queries.GetCustomerById;

/// <summary>
/// Contains the unit tests for the customer detail query handler.
/// These tests verify tenant protection, not-found behavior, and the happy path
/// that returns flattened customer details with address and contact collections.
/// </summary>
public sealed class GetCustomerByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the tenant identifier is missing.
    /// This protects the query from executing without a valid company context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetCustomerByIdQueryHandlerTestContext(Guid.Empty);
        var query = new GetCustomerByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the customer exists for the current tenant.
    /// This test ensures the handler returns the flattened customer payload together
    /// with mapped addresses and contacts from the shared Dapper fake infrastructure.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomerExists_ShouldReturnFlattenedDetails()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = new GetCustomerByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            CreateCustomerDetailResultSet(customerId, companyId),
            CreateAddressResultSet(),
            CreateContactResultSet());

        var query = new GetCustomerByIdQuery(customerId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Customer retrieved successfully.");
        response.Data.Should().NotBeNull();

        var customer = response.Data!;
        customer.Id.Should().Be(customerId);
        customer.CompanyId.Should().Be(companyId);
        customer.PersonType.Should().Be("Physical");
        customer.FirstName.Should().Be("Jane");
        customer.LastName.Should().Be("Doe");
        customer.IdentificationTypeName.Should().Be("Passport");
        customer.IdentificationNumber.Should().Be("ID-12345");

        customer.Addresses.Should().NotBeNull();
        customer.Addresses!.Should().HaveCount(2);
        customer.Addresses.First().AddressLine1.Should().Be("Main street 100");
        customer.Addresses.First().StreetTypeName.Should().Be("Street");
        customer.Addresses.First().CountryName.Should().Be("Nicaragua");
        customer.Addresses.First().CreatedAt.Should().Be("2026-04-19 14:30");

        customer.Contacts.Should().NotBeNull();
        customer.Contacts!.Should().HaveCount(2);
        customer.Contacts.First().ContactType.Should().Be("2");
        customer.Contacts.First().ContactValue.Should().Be("jane@contoso.com");
        customer.Contacts.First().CreatedAt.Should().Be("2026-04-19 15:00");

        context.Connection.LastCommandText.Should().Contain("WHERE c.Id = @Id AND c.CompanyId = @TenantId AND c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("WHERE a.CustomerId = @Id AND a.CompanyId = @TenantId AND a.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("WHERE co.CustomerId = @Id AND co.CompanyId = @TenantId AND co.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(customerId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found branch when the current tenant has no matching customer.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomerDoesNotExist_ShouldReturnCustomerNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = new GetCustomerByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "FirstName",
                "MiddleName",
                "LastName",
                "SecondLastName",
                "PersonType",
                "IdentificationTypeId",
                "IdentificationTypeName",
                "IdentificationNumber",
                "Created"),
            FakeResultSet.Empty(
                "Id",
                "AddressLine1",
                "AddressLine2",
                "ZipCode",
                "IsDefault",
                "StreetTypeId",
                "StreetTypeName",
                "CountryId",
                "CountryName",
                "RegionId",
                "RegionName",
                "ProvinceId",
                "ProvinceName",
                "MunicipalityId",
                "MunicipalityName",
                "Created"),
            FakeResultSet.Empty(
                "Id",
                "ContactType",
                "ContactValue",
                "IsPrimary",
                "Comments",
                "Created"));

        var query = new GetCustomerByIdQuery(customerId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Customer not found.");
        response.Data.Should().BeNull();
    }

    /// <summary>
    /// Creates a fake result set containing one customer detail row.
    /// </summary>
    private static FakeResultSet CreateCustomerDetailResultSet(Guid customerId, Guid companyId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = customerId,
                ["CompanyId"] = companyId,
                ["FirstName"] = "Jane",
                ["MiddleName"] = "Maria",
                ["LastName"] = "Doe",
                ["SecondLastName"] = "Smith",
                ["PersonType"] = "Physical",
                ["IdentificationTypeId"] = Guid.NewGuid(),
                ["IdentificationTypeName"] = "Passport",
                ["IdentificationNumber"] = "ID-12345",
                ["Created"] = new DateTime(2026, 4, 19, 13, 45, 0, DateTimeKind.Utc)
            });
    }

    /// <summary>
    /// Creates a fake result set containing two customer addresses.
    /// </summary>
    private static FakeResultSet CreateAddressResultSet()
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["AddressLine1"] = "Main street 100",
                ["AddressLine2"] = "Floor 2",
                ["ZipCode"] = "11001",
                ["IsDefault"] = true,
                ["StreetTypeId"] = Guid.NewGuid(),
                ["StreetTypeName"] = "Street",
                ["CountryId"] = Guid.NewGuid(),
                ["CountryName"] = "Nicaragua",
                ["RegionId"] = Guid.NewGuid(),
                ["RegionName"] = "Pacific",
                ["ProvinceId"] = Guid.NewGuid(),
                ["ProvinceName"] = "Managua",
                ["MunicipalityId"] = Guid.NewGuid(),
                ["MunicipalityName"] = "Managua",
                ["Created"] = new DateTime(2026, 4, 19, 14, 30, 0, DateTimeKind.Utc)
            },
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["AddressLine1"] = "Secondary avenue 45",
                ["AddressLine2"] = null,
                ["ZipCode"] = "11002",
                ["IsDefault"] = false,
                ["StreetTypeId"] = Guid.NewGuid(),
                ["StreetTypeName"] = "Avenue",
                ["CountryId"] = Guid.NewGuid(),
                ["CountryName"] = "Nicaragua",
                ["RegionId"] = null,
                ["RegionName"] = null,
                ["ProvinceId"] = Guid.NewGuid(),
                ["ProvinceName"] = "León",
                ["MunicipalityId"] = Guid.NewGuid(),
                ["MunicipalityName"] = "León",
                ["Created"] = new DateTime(2026, 4, 19, 14, 45, 0, DateTimeKind.Utc)
            });
    }

    /// <summary>
    /// Creates a fake result set containing two customer contacts.
    /// </summary>
    private static FakeResultSet CreateContactResultSet()
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["ContactType"] = 2,
                ["ContactValue"] = "jane@contoso.com",
                ["IsPrimary"] = true,
                ["Comments"] = "Primary email",
                ["Created"] = new DateTime(2026, 4, 19, 15, 0, 0, DateTimeKind.Utc)
            },
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["ContactType"] = 4,
                ["ContactValue"] = "+50588889999",
                ["IsPrimary"] = false,
                ["Comments"] = "Backup contact",
                ["Created"] = new DateTime(2026, 4, 19, 15, 5, 0, DateTimeKind.Utc)
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the customer detail query tests.
    /// </summary>
    private sealed class GetCustomerByIdQueryHandlerTestContext
    {
        public GetCustomerByIdQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCustomerByIdQueryHandler CreateHandler()
        {
            return new GetCustomerByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
