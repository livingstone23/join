using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleSystemOptions.Queries.GetRoleSystemOptionMatrix;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleSystemOptions.Queries.GetRoleSystemOptionMatrix;

/// <summary>
/// Tests for the role-system-option matrix query handler (SPEC 25).
/// Verifies tenant check, role existence check, and that the matrix is returned
/// unchanged when present, or as ROLE_NOT_FOUND when absent / soft-deleted.
/// </summary>
public sealed class GetRoleSystemOptionMatrixQueryHandlerTests
{
    private static GetRoleSystemOptionMatrixQuery Cmd(Guid? roleId = null) => new(roleId ?? Guid.NewGuid());

    /// <summary>
    /// Empty CompanyId short-circuits before the role check.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTenantEmpty_ShouldReturnTenantRequiredError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
        context.RoleSystemOptionsRepositoryMock.Verify(
            x => x.GetMatrixByRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Missing or inactive role returns ROLE_NOT_FOUND without calling the repo.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleNotFound_ShouldReturnRoleNotFoundError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_NOT_FOUND");
        context.RoleSystemOptionsRepositoryMock.Verify(
            x => x.GetMatrixByRoleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Happy path: a role with two modules and three options (mix of granted/non-granted) flows through unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasPermissionsInTwoModules_ShouldReturnGroupedMatrix()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var supportsAll = new RoleSystemOptionSupportFlags(true, true, true, true, true, true, true);
        var grantedNone = new RoleSystemOptionGrantedFlags(false, false, false, false, false, false, false);
        var grantedRead = new RoleSystemOptionGrantedFlags(true, false, false, false, false, false, false);

        var matrix = new RoleSystemOptionMatrixDto(
            RoleId: roleId,
            RoleName: "Manager",
            Modules: new List<RoleSystemOptionMatrixModuleDto>
            {
                new(Guid.NewGuid(), "CRM", new List<RoleSystemOptionMatrixOptionDto>
                {
                    new(Guid.NewGuid(), "Leads", "/leads", supportsAll, grantedRead),
                    new(Guid.NewGuid(), "Accounts", "/accounts", supportsAll, grantedNone)
                }),
                new(Guid.NewGuid(), "Tickets", new List<RoleSystemOptionMatrixOptionDto>
                {
                    new(Guid.NewGuid(), "Tickets", "/tickets", supportsAll, grantedRead)
                })
            });

        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetMatrixByRoleAsync(roleId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeSameAs(matrix);
        response.Data!.Modules.Should().HaveCount(2);
        response.Data.Modules[0].Options.Should().HaveCount(2);
        response.Data.Modules[1].Options.Should().HaveCount(1);
    }

    /// <summary>
    /// Role exists but has zero granted rows → matrix still contains all options with Granted = all-false.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleHasNoPermissions_ShouldReturnMatrixWithAllGrantedFalse()
    {
        var roleId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var supportsAll = new RoleSystemOptionSupportFlags(true, true, true, true, true, true, true);
        var grantedNone = new RoleSystemOptionGrantedFlags(false, false, false, false, false, false, false);

        var matrix = new RoleSystemOptionMatrixDto(
            RoleId: roleId,
            RoleName: "EmptyRole",
            Modules: new List<RoleSystemOptionMatrixModuleDto>
            {
                new(Guid.NewGuid(), "Module-1", new List<RoleSystemOptionMatrixOptionDto>
                {
                    new(Guid.NewGuid(), "Option-A", "/a", supportsAll, grantedNone),
                    new(Guid.NewGuid(), "Option-B", "/b", supportsAll, grantedNone)
                })
            });

        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleSystemOptionsRepositoryMock
            .Setup(x => x.GetMatrixByRoleAsync(roleId, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(matrix);

        var handler = context.CreateHandler();
        var response = await handler.Handle(Cmd(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Modules.Should().HaveCount(1);
        response.Data.Modules[0].Options.Should().AllSatisfy(o =>
        {
            o.Granted.CanRead.Should().BeFalse();
            o.Granted.CanCreate.Should().BeFalse();
            o.Granted.CanUpdate.Should().BeFalse();
            o.Granted.CanDelete.Should().BeFalse();
            o.Granted.CanDownload.Should().BeFalse();
            o.Granted.CanExport.Should().BeFalse();
            o.Granted.CanExecute.Should().BeFalse();
        });
    }

    private sealed class TestContext
    {
        public Mock<IRoleSystemOptionsRepository> RoleSystemOptionsRepositoryMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRoleSystemOptionMatrixQueryHandler CreateHandler()
            => new(
                RoleSystemOptionsRepositoryMock.Object,
                RoleRepositoryMock.Object,
                CurrentUserServiceMock.Object,
                NullLogger<GetRoleSystemOptionMatrixQueryHandler>.Instance);
    }
}
