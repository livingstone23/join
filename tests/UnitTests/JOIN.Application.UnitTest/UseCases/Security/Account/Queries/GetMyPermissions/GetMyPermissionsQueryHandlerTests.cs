using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Queries.GetMyPermissions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Queries.GetMyPermissions;

/// <summary>
/// Unit tests for the <c>GET /api/v1/account/my-permissions</c> handler (SPEC 26 / F4).
/// </summary>
public sealed class GetMyPermissionsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Two-role happy path: 2 modules, the per-option Granted should be the OR of the two
    /// role's flags, roleIds carries both ids and the matrix roleName is formatted
    /// "<first> + 1 más".
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasTwoRolesAndTwoModules_ShouldReturnCombinedMatrix()
    {
        // Arrange
        var context = new GetMyPermissionsQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var roleA = _fixture.Create<Guid>();
        var roleB = _fixture.Create<Guid>();
        var moduleX = _fixture.Create<Guid>();
        var moduleY = _fixture.Create<Guid>();
        var optionX1 = _fixture.Create<Guid>();
        var optionX2 = _fixture.Create<Guid>();
        var optionY1 = _fixture.Create<Guid>();
        var optionY2 = _fixture.Create<Guid>();

        context.SetUser(callerId);
        context.SetCompanyId(companyId);

        context.SessionRepositoryMock
            .Setup(x => x.ListUserRolesInTenantAsync(callerId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new UserRoleLookupRow(roleA, "Admin"),
                new UserRoleLookupRow(roleB, "Reader")
            });

        // Granted for X1 = OR(roleA.CanRead=true, roleB.CanRead=false) = true.
        // Granted for X2 = OR(roleA.CanRead=false, roleB.CanRead=true) = true.
        var flagGrid = new List<PermissionFlagGridRow>
        {
            new(
                ModuleId: moduleX,
                ModuleName: "CRM",
                SystemOptionId: optionX1,
                OptionName: "Persons.Read",
                OptionRoute: "/persons",
                DisplayOrder: 1,
                Supports: new RoleSystemOptionSupportFlags(true, false, false, false, false, false, false),
                Granted: new RoleSystemOptionGrantedFlags(true, false, false, false, false, false, false)),
            new(
                ModuleId: moduleX,
                ModuleName: "CRM",
                SystemOptionId: optionX2,
                OptionName: "Persons.Export",
                OptionRoute: "/persons/export",
                DisplayOrder: 2,
                Supports: new RoleSystemOptionSupportFlags(true, true, true, true, false, false, false),
                Granted: new RoleSystemOptionGrantedFlags(false, false, false, false, false, false, false)),
            new(
                ModuleId: moduleY,
                ModuleName: "Tickets",
                SystemOptionId: optionY1,
                OptionName: "Tickets.Manage",
                OptionRoute: "/tickets",
                DisplayOrder: 1,
                Supports: new RoleSystemOptionSupportFlags(true, true, true, true, false, false, false),
                Granted: new RoleSystemOptionGrantedFlags(true, false, false, false, false, false, false)),
            new(
                ModuleId: moduleY,
                ModuleName: "Tickets",
                SystemOptionId: optionY2,
                OptionName: "Tickets.Create",
                OptionRoute: "/tickets/create",
                DisplayOrder: 2,
                Supports: new RoleSystemOptionSupportFlags(true, true, true, true, false, false, false),
                Granted: new RoleSystemOptionGrantedFlags(true, true, false, false, false, false, false))
        };

        var passedRoleIds = new List<Guid>();
        context.SessionRepositoryMock
            .Setup(x => x.GetPermissionFlagGridAsync(It.IsAny<IReadOnlyCollection<Guid>>(), companyId, It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, Guid, CancellationToken>((ids, _, _) => passedRoleIds.AddRange(ids))
            .ReturnsAsync(flagGrid);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyPermissionsQuery(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.UserId.Should().Be(callerId);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.RoleIds.Should().BeEquivalentTo(new[] { roleA, roleB });
        response.Data.Matrix.RoleName.Should().Be("Admin + 1 más");
        response.Data.Matrix.Modules.Should().HaveCount(2);

        var xModule = response.Data.Matrix.Modules.Single(m => m.ModuleId == moduleX);
        xModule.Options.Should().HaveCount(2);
        xModule.Options.Single(o => o.SystemOptionId == optionX1).Granted.CanRead.Should().BeTrue();

        var yModule = response.Data.Matrix.Modules.Single(m => m.ModuleId == moduleY);
        yModule.Options.Should().HaveCount(2);
        yModule.Options.Single(o => o.SystemOptionId == optionY2).Granted.CanCreate.Should().BeTrue();

        // The two role ids discovered from the tenant lookup should drive the flag grid query.
        passedRoleIds.Should().BeEquivalentTo(new[] { roleA, roleB });
    }

    /// <summary>
    /// No-roles case: roleIds is empty, roleName is empty, modules are still populated with
    /// every active option, but every Granted flag is false.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasNoRoles_ShouldReturnEmptyRoleIdsAndAllFalseGranted()
    {
        // Arrange
        var context = new GetMyPermissionsQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var companyId = _fixture.Create<Guid>();
        var moduleId = _fixture.Create<Guid>();
        var optionId = _fixture.Create<Guid>();

        context.SetUser(callerId);
        context.SetCompanyId(companyId);

        context.SessionRepositoryMock
            .Setup(x => x.ListUserRolesInTenantAsync(callerId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UserRoleLookupRow>());

        context.SessionRepositoryMock
            .Setup(x => x.GetPermissionFlagGridAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
                companyId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PermissionFlagGridRow(
                    ModuleId: moduleId,
                    ModuleName: "CRM",
                    SystemOptionId: optionId,
                    OptionName: "Persons",
                    OptionRoute: "/persons",
                    DisplayOrder: 1,
                    Supports: new RoleSystemOptionSupportFlags(true, true, true, true, true, true, true),
                    Granted: new RoleSystemOptionGrantedFlags(false, false, false, false, false, false, false))
            });

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyPermissionsQuery(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RoleIds.Should().BeEmpty();
        response.Data.Matrix.RoleName.Should().Be(string.Empty);
        response.Data.Matrix.Modules.Should().ContainSingle();
        var option = response.Data.Matrix.Modules[0].Options[0];
        option.Granted.CanRead.Should().BeFalse();
        option.Granted.CanCreate.Should().BeFalse();
        option.Granted.CanDelete.Should().BeFalse();
        option.Supports.CanRead.Should().BeTrue();
    }

    /// <summary>
    /// Tenant empty: the JWT carries no CompanyId (and no X-Company-Id fallback). Handler
    /// short-circuits with TENANT_REQUIRED.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnTenantRequired()
    {
        // Arrange
        var context = new GetMyPermissionsQueryHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetUser(callerId);
        context.SetCompanyId(Guid.Empty);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMyPermissionsQuery(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
        response.Data.Should().BeNull();

        context.SessionRepositoryMock.Verify(
            x => x.ListUserRolesInTenantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.SessionRepositoryMock.Verify(
            x => x.GetPermissionFlagGridAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Self-contained mocks for the handler dependencies.
    /// </summary>
    private sealed class GetMyPermissionsQueryHandlerTestContext
    {
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IRoleUserSessionRepository> SessionRepositoryMock { get; } = new();

        public void SetUser(Guid userId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
        }

        public void SetCompanyId(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        }

        public GetMyPermissionsQueryHandler CreateHandler() =>
            new(CurrentUserServiceMock.Object, SessionRepositoryMock.Object);
    }
}
