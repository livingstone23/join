using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.Companies.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Companies.Queries.GetCompanyById;

/// <summary>
/// Contains the unit tests for the company detail query.
/// These tests verify the happy path and the not-found response branch.
/// </summary>
public sealed class GetCompanyByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that an existing company is returned successfully.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyExists_ShouldReturnCompanyDto()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetCompanyByIdQueryTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = companyId,
                    ["Name"] = "JOIN CRM",
                    ["Description"] = "CRM Platform",
                    ["TaxId"] = "RUC-123",
                    ["Email"] = "info@join.com",
                    ["Phone"] = "555-1000",
                    ["WebSite"] = "https://join.com",
                    ["IsActive"] = true
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetCompanyByIdQuery(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Company retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(companyId);
        response.Data.Name.Should().Be("JOIN CRM");
        response.Data.TaxId.Should().Be("RUC-123");
        response.Data.IsActive.Should().BeTrue();
        context.Connection.LastCommandText.Should().Contain("WHERE c.Id = @Id AND c.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(companyId);
    }

    /// <summary>
    /// Verifies that a missing company returns the not-found response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetCompanyByIdQueryTestContext();
        context.Connection.SetResults(FakeResultSet.Empty("Id", "Name", "Description", "TaxId", "Email", "Phone", "WebSite", "IsActive"));
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetCompanyByIdQuery(companyId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_NOT_FOUND");
        response.Errors.Should().Contain("Company not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the detail query tests.
    /// </summary>
    private sealed class GetCompanyByIdQueryTestContext
    {
        public GetCompanyByIdQueryTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCompanyByIdQueryHandler CreateHandler()
        {
            return new GetCompanyByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
