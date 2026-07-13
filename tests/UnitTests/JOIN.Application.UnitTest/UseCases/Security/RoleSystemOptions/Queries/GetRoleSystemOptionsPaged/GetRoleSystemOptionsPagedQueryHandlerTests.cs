using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.RoleSystemOptions.Queries.GetRoleSystemOptionsPaged;

/// <summary>
/// Contains unit tests for the tenant-scoped role system options paged query handler.
/// </summary>
public sealed class GetRoleSystemOptionsPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the error branch when the authenticated company context is missing.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
    {
        // Arrange
        var context = new GetRoleSystemOptionsPagedQueryHandlerTestContext(Guid.Empty);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetRoleSystemOptionsPagedQuery(null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    /// <summary>
    /// Verifies the happy path with tenant filter, SQL joins, and pagination.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyContextIsValid_ShouldReturnPagedRoleSystemOptions()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var roleSystemOptionId = _fixture.Create<Guid>();
        var context = new GetRoleSystemOptionsPagedQueryHandlerTestContext(companyId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = roleSystemOptionId,
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "JOIN CRM",
                    ["RoleId"] = _fixture.Create<Guid>(),
                    ["RoleName"] = "Admin",
                    ["SystemOptionId"] = _fixture.Create<Guid>(),
                    ["SystemOptionName"] = "Users",
                    ["CanRead"] = true,
                    ["CanCreate"] = false,
                    ["CanUpdate"] = true,
                    ["CanDelete"] = false,
                    ["Created"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                }),
            FakeResultSet.FromScalar(1));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetRoleSystemOptionsPagedQuery(1, 10, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Role system options retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.Items.First().Id.Should().Be(roleSystemOptionId);
        response.Data.TotalCount.Should().Be(1);

        context.Connection.LastCommandText.Should().Contain("FROM Security.RoleSystemOptions rso");
        context.Connection.LastCommandText.Should().Contain("INNER JOIN Security.Roles ar");
        context.Connection.LastCommandText.Should().Contain("LEFT JOIN Common.Companies c");
        context.Connection.LastCommandText.Should().Contain("AND rso.CompanyId = @CompanyId");
        context.Connection.LastCommandText.Should().Contain("OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    private sealed class GetRoleSystemOptionsPagedQueryHandlerTestContext
    {
        public GetRoleSystemOptionsPagedQueryHandlerTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 10,
                MaxPageSize = 50
            });
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetRoleSystemOptionsPagedQueryHandler CreateHandler()
        {
            return new GetRoleSystemOptionsPagedQueryHandler(
                ConnectionFactoryMock.Object,
                CurrentUserServiceMock.Object,
                PaginationOptions);
        }
    }
}
