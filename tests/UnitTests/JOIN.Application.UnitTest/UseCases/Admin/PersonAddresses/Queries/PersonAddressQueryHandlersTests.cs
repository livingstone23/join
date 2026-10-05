using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonAddresses.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Queries;

/// <summary>
/// Contains the unit tests for the person address Dapper query handlers.
/// </summary>
public sealed class PersonAddressQueryHandlersTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly string[] Columns =
    [
        "Id", "PersonId", "AddressLine1", "AddressLine2", "ZipCode", "StreetTypeId", "StreetTypeName", "CountryId", "CountryName",
        "RegionId", "RegionName", "ProvinceId", "ProvinceName", "MunicipalityId", "MunicipalityName", "IsDefault", "Created"
    ];

    [Fact]
    public async Task GetById_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByIdHandler().Handle(new GetPersonAddressByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetById_WhenAddressIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.Empty(Columns));

        var response = await context.CreateByIdHandler().Handle(new GetPersonAddressByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_ADDRESS_NOT_FOUND");
    }

    [Fact]
    public async Task GetById_WhenAddressExists_ShouldMapCatalogNamesAndFormatCreatedAt()
    {
        var id = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(Row(id, addressLine1: "Main St", zipCode: "1000")));

        var response = await context.CreateByIdHandler().Handle(new GetPersonAddressByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be(id);
        response.Data.AddressLine1.Should().Be("Main St");
        response.Data.CountryName.Should().Be("Panama");
        response.Data.ProvinceName.Should().Be("Panama Oeste");
        response.Data.MunicipalityName.Should().Be("Arraijan");
        response.Data.CreatedAt.Should().Be("2026-05-01 10:30");
        context.Connection.CapturedParameters["TenantId"].Should().Be(CompanyId);
    }

    [Fact]
    public async Task GetByPersonId_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateByPersonHandler().Handle(new GetPersonAddressesByPersonIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task GetByPersonId_ShouldReturnRowsWithNullFallbacks()
    {
        var personId = Guid.NewGuid();
        var context = new TestContext(CompanyId);
        context.Connection.SetResults(FakeResultSet.FromRows(
            Row(Guid.NewGuid(), addressLine1: "A", zipCode: "1"),
            Row(Guid.NewGuid(), addressLine1: null, zipCode: null)));

        var response = await context.CreateByPersonHandler().Handle(new GetPersonAddressesByPersonIdQuery(personId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Data![1].AddressLine1.Should().BeEmpty();
        response.Data[1].ZipCode.Should().BeEmpty();
        context.Connection.LastCommandText.Should().Contain("ORDER BY a.IsDefault DESC");
        context.Connection.CapturedParameters["PersonId"].Should().Be(personId);
    }

    private static Dictionary<string, object?> Row(Guid id, string? addressLine1, string? zipCode) => new()
    {
        ["Id"] = id,
        ["PersonId"] = Guid.NewGuid(),
        ["AddressLine1"] = addressLine1,
        ["AddressLine2"] = null,
        ["ZipCode"] = zipCode,
        ["StreetTypeId"] = Guid.NewGuid(),
        ["StreetTypeName"] = "Calle",
        ["CountryId"] = Guid.NewGuid(),
        ["CountryName"] = "Panama",
        ["RegionId"] = null,
        ["RegionName"] = null,
        ["ProvinceId"] = Guid.NewGuid(),
        ["ProvinceName"] = "Panama Oeste",
        ["MunicipalityId"] = Guid.NewGuid(),
        ["MunicipalityName"] = "Arraijan",
        ["IsDefault"] = true,
        ["Created"] = new DateTime(2026, 5, 1, 10, 30, 0, DateTimeKind.Utc)
    };

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetPersonAddressByIdQueryHandler CreateByIdHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);

        public GetPersonAddressesByPersonIdQueryHandler CreateByPersonHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}
