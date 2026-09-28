using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.TestDoubles;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetSystemWideTicketUserCompanies;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Queries.GetSystemWideTicketUserCompanies;

/// <summary>
/// Unit tests for <see cref="GetSystemWideTicketUserCompaniesQueryHandler"/>:
/// cross-tenant browsing and CompanyName LIKE filter.
/// </summary>
public sealed class GetSystemWideTicketUserCompaniesQueryHandlerTests
{
    private readonly Fixture _fixture = new();
    private readonly PaginationSettings _pagination = new();

    [Fact]
    public async Task Handle_ShouldNotFilterByTenant()
    {
        var context = new GetSystemWideTicketUserCompaniesQueryHandlerTestContext(_pagination);
        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = _fixture.Create<Guid>(),
                    ["CompanyId"] = _fixture.Create<Guid>(),
                    ["CompanyName"] = "JOIN",
                    ["UserId"] = _fixture.Create<Guid>(),
                    ["UserName"] = "Manager JOIN",
                    ["UserEmail"] = "manager@join.com",
                    ["IsSuperAdminTicket"] = true,
                    ["CanFinishTicket"] = true,
                    ["CanResolveTicket"] = true,
                    ["CreatedAt"] = DateTime.UtcNow
                },
                new Dictionary<string, object?>
                {
                    ["Id"] = _fixture.Create<Guid>(),
                    ["CompanyId"] = _fixture.Create<Guid>(),
                    ["CompanyName"] = "Other",
                    ["UserId"] = _fixture.Create<Guid>(),
                    ["UserName"] = "Manager Other",
                    ["UserEmail"] = "manager@other.com",
                    ["IsSuperAdminTicket"] = true,
                    ["CanFinishTicket"] = false,
                    ["CanResolveTicket"] = true,
                    ["CreatedAt"] = DateTime.UtcNow
                }),
            FakeResultSet.FromScalar(2));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetSystemWideTicketUserCompaniesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(2);
        response.Data.Items.Should().HaveCount(2);
        context.Connection.LastCommandText.Should().NotContain("@TenantId");
        context.Connection.LastCommandText.Should().Contain("WHERE tuc.GcRecord = 0");
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
    }

    [Fact]
    public async Task Handle_WhenCompanyNameProvided_ShouldAppendLikeFilter()
    {
        var context = new GetSystemWideTicketUserCompaniesQueryHandlerTestContext(_pagination);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "UserId", "UserName", "UserEmail", "IsSuperAdminTicket", "CanFinishTicket", "CanResolveTicket", "CreatedAt"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(
            new GetSystemWideTicketUserCompaniesQuery { CompanyName = "JOIN" },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Connection.LastCommandText.Should().Contain("AND co.Name LIKE @CompanyName");
        context.Connection.CapturedParameters["CompanyName"].Should().Be("%JOIN%");
    }

    private sealed class GetSystemWideTicketUserCompaniesQueryHandlerTestContext
    {
        public GetSystemWideTicketUserCompaniesQueryHandlerTestContext(PaginationSettings pagination)
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(pagination);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetSystemWideTicketUserCompaniesQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, PaginationOptions);
    }
}
