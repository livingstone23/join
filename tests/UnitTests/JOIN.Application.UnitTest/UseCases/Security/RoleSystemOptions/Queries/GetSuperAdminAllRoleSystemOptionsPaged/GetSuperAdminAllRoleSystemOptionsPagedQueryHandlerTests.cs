using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.RoleSystemOptions.Queries.GetSuperAdminAllRoleSystemOptionsPaged;

/// <summary>
/// Contains unit tests for the SuperAdmin role system options paged query handler.
/// </summary>
public sealed class GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that no company filter is applied when CompanyId is not provided.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsNotProvided_ShouldQueryAcrossAllCompanies()
    {
        // Arrange
        var context = new GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "CompanyId",
                "CompanyName",
                "RoleId",
                "RoleName",
                "SystemOptionId",
                "SystemOptionName",
                "CanRead",
                "CanCreate",
                "CanUpdate",
                "CanDelete",
                "Created"),
            FakeResultSet.FromScalar(0));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetSuperAdminAllRoleSystemOptionsPagedQuery(null, null, null, null, null, null, null, null, null, null, null, null),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().BeEmpty();
        context.Connection.LastCommandText.Should().NotContain("AND rso.CompanyId = @CompanyId");
    }

    /// <summary>
    /// Verifies that an optional CompanyId filter is applied for SuperAdmin requests.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsProvided_ShouldFilterByCompany()
    {
        // Arrange
        var companyId = _fixture.Create<Guid>();
        var context = new GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = _fixture.Create<Guid>(),
                    ["CompanyId"] = companyId,
                    ["CompanyName"] = "Tenant A",
                    ["RoleId"] = _fixture.Create<Guid>(),
                    ["RoleName"] = "Manager",
                    ["SystemOptionId"] = _fixture.Create<Guid>(),
                    ["SystemOptionName"] = "Tickets",
                    ["CanRead"] = true,
                    ["CanCreate"] = true,
                    ["CanUpdate"] = false,
                    ["CanDelete"] = false,
                    ["Created"] = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                }),
            FakeResultSet.FromScalar(1));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(
            new GetSuperAdminAllRoleSystemOptionsPagedQuery(1, 10, null, null, null, null, null, null, null, null, null, companyId),
            CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.Items.First().CompanyId.Should().Be(companyId);
        context.Connection.LastCommandText.Should().Contain("AND rso.CompanyId = @CompanyId");
        context.Connection.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    private sealed class GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTestContext
    {
        public GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
            PaginationOptions = Options.Create(new PaginationSettings
            {
                DefaultPageNumber = 1,
                DefaultPageSize = 10,
                MaxPageSize = 50
            });
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();
        public IOptions<PaginationSettings> PaginationOptions { get; }

        public GetSuperAdminAllRoleSystemOptionsPagedQueryHandler CreateHandler()
        {
            return new GetSuperAdminAllRoleSystemOptionsPagedQueryHandler(
                ConnectionFactoryMock.Object,
                PaginationOptions);
        }
    }
}
