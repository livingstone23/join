using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Users.Queries.GetUserEffectivePermissions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Queries.GetUserEffectivePermissions;

/// <summary>
/// SPEC 28 / F5 acceptance criteria for <see cref="GetUserEffectivePermissionsQueryHandler"/>:
/// tenant guard, user existence guard, OR semantics, empty-roles case, and the
/// no-cache requirement.
/// </summary>
public sealed class GetUserEffectivePermissionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ShouldReturnTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var response = await ctx.Handler.Handle(
            new GetUserEffectivePermissionsQuery(Guid.NewGuid()),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenSnapshotIsNull_ShouldReturnUserNotFound()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetAdminSnapshotAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAdminSnapshot?)null);

        var response = await ctx.Handler.Handle(
            new GetUserEffectivePermissionsQuery(userId),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRepoReturnsNullDespiteValidSnapshot_ShouldReturnUserNotFound()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetAdminSnapshotAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(userId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: true));
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetEffectivePermissionsAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserEffectivePermissionsDto?)null);

        var response = await ctx.Handler.Handle(
            new GetUserEffectivePermissionsQuery(userId),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("USER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenUserHasNoRoles_ShouldReturnDtoWithEmptyRoleListsAndFullGrid()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetAdminSnapshotAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAdminSnapshot(userId, "u@x.com", "U", IsActive: true, GcRecord: 0, HasMembership: true));
        ctx.UserAdminRepositoryMock
            .Setup(x => x.GetEffectivePermissionsAsync(userId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserEffectivePermissionsDto(
                UserId: userId,
                CompanyId: companyId,
                RoleIds: Array.Empty<Guid>(),
                RoleNames: Array.Empty<string>(),
                Modules: new List<RoleSystemOptionMatrixModuleDto>
                {
                    new(Guid.NewGuid(), "Module A", new List<RoleSystemOptionMatrixOptionDto>
                    {
                        new(Guid.NewGuid(), "Op", "/route",
                            new RoleSystemOptionSupportFlags(true, true, true, true, true, true, true),
                            new RoleSystemOptionGrantedFlags(false, false, false, false, false, false, false))
                    })
                }));

        var response = await ctx.Handler.Handle(
            new GetUserEffectivePermissionsQuery(userId),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.RoleIds.Should().BeEmpty();
        response.Data.RoleNames.Should().BeEmpty();
        response.Data.Modules.Should().HaveCount(1);
        response.Data.Modules[0].Options[0].Granted.CanRead.Should().BeFalse();
    }

    private sealed class Context
    {
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetUserEffectivePermissionsQueryHandler Handler => new(
            UserAdminRepositoryMock.Object,
            CurrentUserServiceMock.Object);
    }
}