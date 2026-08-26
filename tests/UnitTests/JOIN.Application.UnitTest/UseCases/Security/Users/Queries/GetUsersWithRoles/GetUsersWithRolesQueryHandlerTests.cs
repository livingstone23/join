using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Users.Queries.GetUsersWithRoles;
using JOIN.Application.UnitTest.Common.TestDoubles;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Queries.GetUsersWithRoles;

/// <summary>
/// SPEC 28 / F2 acceptance criteria for <see cref="GetUsersWithRolesQueryHandler"/>:
/// tenant scoping, empty tenant guard, and SQL parameterized with the tenant id.
/// </summary>
public sealed class GetUsersWithRolesQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(new GetUsersWithRolesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenUsersExist_ShouldGroupRowsAndReturnUsersWithTheirRoles()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleName = "Admin";
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);

        // Detail query emits one row per (user, role) — handler groups in C#.
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = userId,
                ["UserName"] = "jdoe",
                ["Email"] = "jdoe@joincrm.com",
                ["IsActive"] = true,
                ["RoleName"] = roleName
            }
        ));

        var response = await ctx.Handler.Handle(new GetUsersWithRolesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().HaveCount(1);
        response.Data!.First().Id.Should().Be(userId);
        response.Data!.First().Roles.Should().ContainSingle().Which.Should().Be(roleName);
        ctx.ConnectionFactory.CapturedParameters.Should().ContainKey("CompanyId");
        ctx.ConnectionFactory.CapturedParameters["CompanyId"].Should().Be(companyId);
    }

    [Fact]
    public async Task Handle_WhenUserHasNoRoles_ShouldAppearInListWithEmptyRoles()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);

        // LEFT JOIN produces a row with RoleName = null when the user has no roles.
        ctx.ConnectionFactory.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = userId,
                ["UserName"] = "lone",
                ["Email"] = "lone@joincrm.com",
                ["IsActive"] = true,
                ["RoleName"] = null
            }
        ));

        var response = await ctx.Handler.Handle(new GetUsersWithRolesQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Should().HaveCount(1);
        response.Data!.First().Roles.Should().BeEmpty();
    }

    private sealed class Context
    {
        public FakeSqlConnectionFactory ConnectionFactory { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetUsersWithRolesQueryHandler Handler => new(
            ConnectionFactory,
            CurrentUserServiceMock.Object);
    }
}
