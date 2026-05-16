using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Persons.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Queries.GetPersonById;

/// <summary>
/// Contains the unit tests for the customer detail query handler.
/// These tests verify tenant protection, not-found behavior, and the happy path
/// that returns person details with related collections.
/// </summary>
public sealed class GetPersonByIdQueryHandlerTests
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
        var context = new GetPersonByIdQueryHandlerTestContext(Guid.Empty);
        var query = new GetPersonByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        response.Errors.Should().Contain("The authenticated token must contain a valid CompanyId claim.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies the happy path when the customer exists for the current tenant.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonExists_ShouldReturnPersonDetailWithCollections()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = new GetPersonByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            CreatePersonDetailResultSet(customerId, companyId),
            CreateAddressResultSet(),
            CreateContactResultSet(),
            FakeResultSet.Empty("Id", "EmployerName", "JobTitle", "StartDate", "EndDate", "IsCurrent", "IsActive"),
            FakeResultSet.Empty("Id", "IndustryId", "IndustryName", "TaxRegimeId", "TaxRegimeName", "Website", "FoundationDate", "IsActive"),
            FakeResultSet.Empty("Id", "IncomeRangeId", "IncomeRangeName", "SourceOfFunds", "DeclaredDate", "IsCurrent", "IsActive"));

        var query = new GetPersonByIdQuery(customerId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person retrieved successfully.");
        response.Data.Should().NotBeNull();

        var detail = response.Data!;
        detail.Person.Id.Should().Be(customerId);
        detail.Person.CompanyId.Should().Be(companyId);
        detail.Person.CompanyName.Should().Be("JOIN Software Group");
        detail.Person.PersonType.Should().Be("1");
        detail.Person.PersonTypeName.Should().Be("Natural");
        detail.Person.FirstName.Should().Be("Jane");
        detail.Person.LastName.Should().Be("Doe");
        detail.Person.IdentificationTypeName.Should().Be("Passport");
        detail.Person.IdentificationNumber.Should().Be("ID-12345");

        detail.Addresses.Should().NotBeNull();
        detail.Addresses!.Should().HaveCount(2);
        detail.Addresses.First().AddressLine1.Should().Be("Main street 100");
        detail.Addresses.First().StreetTypeName.Should().Be("Street");
        detail.Addresses.First().CountryName.Should().Be("Nicaragua");
        detail.Addresses.First().CreatedAt.Should().Be("2026-04-19 14:30");

        detail.Contacts.Should().NotBeNull();
        detail.Contacts!.Should().HaveCount(2);
        detail.Contacts.First().ContactType.Should().Be("2");
        detail.Contacts.First().ContactName.Should().Be("Correo Alternativo");
        detail.Contacts.First().ContactValue.Should().Be("jane@contoso.com");
        detail.Contacts.Last().ContactType.Should().Be("4");
        detail.Contacts.Last().ContactName.Should().Be("Teléfono Fijo");
        detail.Contacts.First().CreatedAt.Should().Be("2026-04-19 15:00");

        detail.Employments.Should().BeNull();
        detail.BusinessProfiles.Should().BeNull();
        detail.FinancialProfiles.Should().BeNull();

        context.Connection.LastCommandText.Should().Contain("WHERE c.Id = @Id AND c.CompanyId = @TenantId AND c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("WHERE a.PersonId = @Id AND a.CompanyId = @TenantId AND a.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("WHERE pc.PersonId = @Id AND pc.CompanyId = @TenantId AND pc.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(customerId);
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies the not-found branch when the current tenant has no matching customer.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPersonDoesNotExist_ShouldReturnPersonNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var customerId = _fixture.Create<Guid>();
        var context = new GetPersonByIdQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "PersonType",
                "PersonTypeName",
                "GenderId",
                "GenderName",
                "IsActive",
                "FirstName",
                "MiddleName",
                "LastName",
                "SecondLastName",
                "CommercialName",
                "IdentificationTypeId",
                "IdentificationTypeName",
                "IdentificationNumber"),
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
                "Created"),
            FakeResultSet.Empty("Id", "EmployerName", "JobTitle", "StartDate", "EndDate", "IsCurrent", "IsActive"),
            FakeResultSet.Empty("Id", "IndustryId", "IndustryName", "TaxRegimeId", "TaxRegimeName", "Website", "FoundationDate", "IsActive"),
            FakeResultSet.Empty("Id", "IncomeRangeId", "IncomeRangeName", "SourceOfFunds", "DeclaredDate", "IsCurrent", "IsActive"));

        var query = new GetPersonByIdQuery(customerId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("PERSON_NOT_FOUND");
        response.Data.Should().BeNull();
    }

    private static FakeResultSet CreatePersonDetailResultSet(Guid customerId, Guid companyId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = customerId,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN Software Group",
                ["PersonType"] = 1,
                ["PersonTypeName"] = "Natural",
                ["GenderId"] = Guid.NewGuid(),
                ["GenderName"] = "Masculino",
                ["IsActive"] = true,
                ["FirstName"] = "Jane",
                ["MiddleName"] = "Maria",
                ["LastName"] = "Doe",
                ["SecondLastName"] = "Smith",
                ["CommercialName"] = null,
                ["IdentificationTypeId"] = Guid.NewGuid(),
                ["IdentificationTypeName"] = "Passport",
                ["IdentificationNumber"] = "ID-12345"
            });
    }

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

    private sealed class GetPersonByIdQueryHandlerTestContext
    {
        public GetPersonByIdQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetPersonByIdQueryHandler CreateHandler()
        {
            return new GetPersonByIdQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
