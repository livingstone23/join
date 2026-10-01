using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.TaxRegimes.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.TaxRegimes.Queries.GetTaxRegimes;

/// <summary>
/// Contains the unit tests for the paged tax regime listing query handler.
/// </summary>
public sealed class GetTaxRegimesQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty, useNpgsql: false);

        var response = await context.CreateHandler().Handle(new GetTaxRegimesQuery(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndPostgresPagination()
    {
        var context = new TestContext(CompanyId, useNpgsql: true);
        context.Connection.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = CompanyId,
                ["CompanyName"] = "JOIN CRM",
                ["Code"] = "M",
                ["Name"] = "Masculino",
                ["IsActive"] = true,
                ["CreatedAt"] = DateTime.UtcNow
            }),
            FakeResultSet.FromScalar(21));

        var response = await context.CreateHandler().Handle(
            new GetTaxRegimesQuery(PageNumber: 2, PageSize: 10, Code: " M ", Name: " Mas ", IsActive: true),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().ContainSingle();
        response.Data.TotalCount.Should().Be(21);
        response.Data.TotalPages.Should().Be(3);
        context.Connection.LastCommandText.Should()
            .Contain("tr.Code LIKE @Code").And.Contain("tr.Name LIKE @Name").And.Contain("tr.IsActive = @IsActive")
            .And.Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["Code"].Should().Be("%M%");
        context.Connection.CapturedParameters["Name"].Should().Be("%Mas%");
        context.Connection.CapturedParameters["Offset"].Should().Be(10);
    }

    [Fact]
    public async Task Handle_WhenNothingMatches_ShouldReturnEmptyPageWithSqlServerPagination()
    {
        var context = new TestContext(CompanyId, useNpgsql: false);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "Code", "Name", "IsActive", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var response = await context.CreateHandler().Handle(new GetTaxRegimesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalPages.Should().Be(0);
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId, bool useNpgsql)
        {
            Connection = useNpgsql ? new FakeNpgsqlDbConnection() : new FakeDbConnection();
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; }

        public GetTaxRegimesQueryHandler CreateHandler()
            => new(
                ConnectionFactoryMock.Object,
                Options.Create(new PaginationSettings { DefaultPageNumber = 1, DefaultPageSize = 10, MaxPageSize = 50 }),
                CurrentUserServiceMock.Object);
    }
}
