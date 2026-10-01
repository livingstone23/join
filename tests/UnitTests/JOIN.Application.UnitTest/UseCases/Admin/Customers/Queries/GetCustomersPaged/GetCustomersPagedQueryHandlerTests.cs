using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Admin.Customers.Queries.GetCustomerById;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.Customers.Queries;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Queries.GetCustomersPaged;

/// <summary>
/// Contains the unit tests for the paged customer listing query handler.
/// </summary>
public sealed class GetCustomersPagedQueryHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty, useNpgsql: false);

        var response = await context.CreateHandler().Handle(new GetCustomersPagedQuery(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenAllFiltersAreProvided_ShouldApplyEveryFilter()
    {
        var context = new TestContext(CompanyId, useNpgsql: true);
        context.Connection.SetResults(
            FakeResultSet.FromRows(GetCustomerByIdQueryHandlerTests.Row(Guid.NewGuid(), CompanyId)),
            FakeResultSet.FromScalar(1));

        var response = await context.CreateHandler().Handle(
            new GetCustomersPagedQuery(
                PageNumber: 1,
                PageSize: 10,
                CustomerCode: " C0 ",
                PersonLifecycleStage: PersonLifecycleStage.Customer,
                IsActive: true,
                PersonName: " Ana ",
                UserEmail: " join "),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Customers retrieved successfully.");
        response.Data!.Items.Should().ContainSingle();
        response.Data.TotalPages.Should().Be(1);
        context.Connection.LastCommandText.Should()
            .Contain("cust.CustomerCode LIKE @CustomerCode")
            .And.Contain("cust.PersonLifecycleStage = @PersonLifecycleStage")
            .And.Contain("cust.IsActive = @IsActive")
            .And.Contain("LIKE @PersonName")
            .And.Contain("u.Email LIKE @UserEmail")
            .And.Contain("LIMIT @PageSize OFFSET @Offset");
        context.Connection.CapturedParameters["CustomerCode"].Should().Be("%C0%");
        context.Connection.CapturedParameters["PersonLifecycleStage"].Should().Be(3);
        context.Connection.CapturedParameters["PersonName"].Should().Be("%Ana%");
        context.Connection.CapturedParameters["UserEmail"].Should().Be("%join%");
    }

    [Fact]
    public async Task Handle_WhenNothingMatches_ShouldReturnEmptyPage()
    {
        var context = new TestContext(CompanyId, useNpgsql: false);
        context.Connection.SetResults(
            FakeResultSet.Empty(GetCustomerByIdQueryHandlerTests.Columns),
            FakeResultSet.FromScalar(0));

        var response = await context.CreateHandler().Handle(new GetCustomersPagedQuery(), CancellationToken.None);

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

        public GetCustomersPagedQueryHandler CreateHandler()
            => new(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object,
                Options.Create(new PaginationSettings { DefaultPageNumber = 1, DefaultPageSize = 10, MaxPageSize = 50 }));
    }
}
