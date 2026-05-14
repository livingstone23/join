using AutoFixture;
using FluentAssertions;
using JOIN.Application.UseCases.Admin.Persons.Queries;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Queries.GetPersonsPaged;

/// <summary>
/// Contains the unit tests for the paged customer listing query.
/// These tests verify tenant isolation, dynamic filtering, and pagination behavior
/// using the fake Dapper database infrastructure shared across the query test suites.
/// </summary>
public sealed class GetPersonsPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the early exit when the company identifier is missing.
    /// This prevents the handler from reading customer data without a valid tenant context.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        // Arrange
        var context = new GetPersonsPagedQueryTestContext(Guid.Empty, useNpgsqlConnection: false);
        var query = new GetPersonsPagedQuery();
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CompanyId header or claim is required.");
        response.Errors.Should().Contain("The X-Company-Id header is required.");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    /// <summary>
    /// Verifies that the handler applies tenant restriction, requested filters,
    /// and sanitized pagination settings when returning a paged customer list.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyTenantFiltersAndReturnPagedPersons()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var identificationTypeId = _fixture.Create<Guid>();
        var context = new GetPersonsPagedQueryTestContext(companyId, useNpgsqlConnection: true);

        context.Connection.SetResults(
            CreatePersonItemsResultSet(companyId, identificationTypeId),
            FakeResultSet.FromScalar(51));

        var query = new GetPersonsPagedQuery(
            PageNumber: 0,
            PageSize: 100,
            PersonType: "  Physical  ",
            FirstName: "  Jane  ",
            LastName: " Doe ",
            CommercialName: "  Contoso  ",
            IdentificationTypeId: identificationTypeId,
            IdentificationNumber: "  12345  ");

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Persons retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(50);
        response.Data.TotalCount.Should().Be(51);
        response.Data.TotalPages.Should().Be(2);

        var item = response.Data.Items.Single();
        item.CompanyId.Should().Be(companyId);
        item.PersonType.Should().Be("Physical");
        item.FirstName.Should().Be("Jane");
        item.LastName.Should().Be("Doe");
        item.CommercialName.Should().Be("Contoso LLC");
        item.IdentificationTypeName.Should().Be("Passport");

        context.Connection.LastCommandText.Should().Contain("WHERE c.CompanyId = @TenantId AND c.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("c.PersonType LIKE @PersonType");
        context.Connection.LastCommandText.Should().Contain("c.FirstName LIKE @FirstName");
        context.Connection.LastCommandText.Should().Contain("c.LastName LIKE @LastName");
        context.Connection.LastCommandText.Should().Contain("c.CommercialName LIKE @CommercialName");
        context.Connection.LastCommandText.Should().Contain("c.IdentificationTypeId = @IdentificationTypeId");
        context.Connection.LastCommandText.Should().Contain("c.IdentificationNumber LIKE @IdentificationNumber");
        context.Connection.LastCommandText.Should().Contain("LIMIT @PageSize OFFSET @Offset");

        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
        context.Connection.CapturedParameters["PersonType"].Should().Be("%Physical%");
        context.Connection.CapturedParameters["FirstName"].Should().Be("%Jane%");
        context.Connection.CapturedParameters["LastName"].Should().Be("%Doe%");
        context.Connection.CapturedParameters["CommercialName"].Should().Be("%Contoso%");
        context.Connection.CapturedParameters["IdentificationTypeId"].Should().Be(identificationTypeId);
        context.Connection.CapturedParameters["IdentificationNumber"].Should().Be("%12345%");
        context.Connection.CapturedParameters["Offset"].Should().Be(0);
        context.Connection.CapturedParameters["PageSize"].Should().Be(50);
    }

    /// <summary>
    /// Verifies that an empty query result still returns a valid paged payload
    /// with default pagination values for invalid incoming numbers.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoPersonsMatch_ShouldReturnEmptyPagedResult()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetPersonsPagedQueryTestContext(companyId, useNpgsqlConnection: false);

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "PersonType",
                "FirstName",
                "MiddleName",
                "LastName",
                "SecondLastName",
                "CommercialName",
                "IdentificationTypeId",
                "IdentificationTypeName",
                "IdentificationNumber"),
            FakeResultSet.FromScalar(0));

        var query = new GetPersonsPagedQuery(PageNumber: -5, PageSize: 0, FirstName: "missing");
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(10);
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        context.Connection.CapturedParameters["TenantId"].Should().Be(companyId);
        context.Connection.CapturedParameters["Offset"].Should().Be(0);
        context.Connection.CapturedParameters["PageSize"].Should().Be(10);
    }

    /// <summary>
    /// Creates a fake result set containing a single paged customer list item.
    /// </summary>
    private static FakeResultSet CreatePersonItemsResultSet(Guid companyId, Guid identificationTypeId)
    {
        return FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["PersonType"] = "Physical",
                ["FirstName"] = "Jane",
                ["MiddleName"] = "Maria",
                ["LastName"] = "Doe",
                ["SecondLastName"] = "Smith",
                ["CommercialName"] = "Contoso LLC",
                ["IdentificationTypeId"] = identificationTypeId,
                ["IdentificationTypeName"] = "Passport",
                ["IdentificationNumber"] = "12345"
            });
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the paged customer query tests.
    /// </summary>
    private sealed class GetPersonsPagedQueryTestContext
    {
        public GetPersonsPagedQueryTestContext(Guid companyId, bool useNpgsqlConnection)
        {
            Connection = useNpgsqlConnection ? new FakeNpgsqlDbConnection() : new FakeDbConnection();

            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetPersonsPagedQueryHandler CreateHandler()
        {
            return new GetPersonsPagedQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
