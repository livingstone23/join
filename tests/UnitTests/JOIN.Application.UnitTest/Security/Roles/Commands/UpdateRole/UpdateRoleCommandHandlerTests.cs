using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.UpdateRole;

/// <summary>
/// Tests for the role update command handler. Covers all three system-default guards, rename collision, and the happy path.
/// </summary>
public sealed class UpdateRoleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Empty CompanyId short-circuits the handler.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnError()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(Guid.NewGuid(), "Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Role not found returns the canonical 404 message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleMissing_ShouldReturnNotFoundMessage()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApplicationRole?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(Guid.NewGuid(), "Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Rol no encontrado o inactivo.");
    }

    /// <summary>
    /// Renaming a system-default role is blocked with the descriptive ≥50-char message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemDefaultRoleIsRenamed_ShouldReturnGuardError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Admin",
                NormalizedName = "ADMIN",
                IsSystemDefault = true,
                GcRecord = 0
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "SuperAdmin", null, true), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Length.Should().BeGreaterThanOrEqualTo(50);
        response.Message.Should().Contain("nombre");
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Demoting a system-default role to non-system-default is blocked.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSystemDefaultRoleIsDemoted_ShouldReturnGuardError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Admin",
                NormalizedName = "ADMIN",
                IsSystemDefault = true,
                GcRecord = 0
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Length.Should().BeGreaterThanOrEqualTo(50);
        response.Message.Should().Contain("sistema");
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Promoting a custom role to system-default is blocked.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCustomRoleIsPromoted_ShouldReturnGuardError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Custom",
                NormalizedName = "CUSTOM",
                IsSystemDefault = false,
                GcRecord = 0
            });

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "Custom", null, true), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Length.Should().BeGreaterThanOrEqualTo(50);
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Renaming into another role's name returns the 409 conflict message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRenameCollidesWithAnotherRole_ShouldReturnConflict()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Old",
                NormalizedName = "OLD",
                IsSystemDefault = false,
                GcRecord = 0
            });
        context.RoleRepositoryMock
            .Setup(x => x.ExistsByNameExceptIdAsync("NEW", roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "New", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().StartWith("Ya existe otro rol con el nombre");
        context.RoleRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Happy path: rename without collision commits the change and returns the mapped DTO.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRenameIsUnique_ShouldUpdateAndReturnDto()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Old",
                NormalizedName = "OLD",
                IsSystemDefault = false,
                GcRecord = 0
            });
        context.RoleRepositoryMock
            .Setup(x => x.ExistsByNameExceptIdAsync("NEW", roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        ApplicationRole? captured = null;
        context.RoleRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var dto = new RoleDto(roleId, "New", "NEW", "desc", false, "user-1", DateTime.UtcNow);
        context.RoleMapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns(dto);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "  New  ", "desc", false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("New");
        captured.NormalizedName.Should().Be("NEW");
        captured.Description.Should().Be("desc");
        captured.LastModifiedBy.Should().Be("user-1");
        response.Data.Should().Be(dto);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 produces an error response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnError()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "Same",
                NormalizedName = "SAME",
                IsSystemDefault = false,
                GcRecord = 0
            });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, "Same", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Updating only the description on a system-default role succeeds when Name is null.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyDescriptionProvidedOnSystemDefault_ShouldUpdateWithoutRenaming()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplicationRole
            {
                Id = roleId,
                Name = "SuperAdmin",
                NormalizedName = "SUPERADMIN",
                IsSystemDefault = true,
                GcRecord = 0
            });
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        ApplicationRole? captured = null;
        context.RoleRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var dto = new RoleDto(roleId, "SuperAdmin", "SUPERADMIN", "New description", true, "user-1", DateTime.UtcNow);
        context.RoleMapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns(dto);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new UpdateRoleCommand(roleId, null, "New description", true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("SuperAdmin");
        captured.NormalizedName.Should().Be("SUPERADMIN");
        captured.Description.Should().Be("New description");
        captured.IsSystemDefault.Should().BeTrue();
        captured.LastModifiedBy.Should().Be("user-1");
        context.RoleRepositoryMock.Verify(x => x.ExistsByNameExceptIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<IRoleMapper> RoleMapperMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IAuditLogger> AuditLoggerMock { get; } = new();

        public UpdateRoleCommandHandler CreateHandler()
        {
            return new UpdateRoleCommandHandler(
                UnitOfWorkMock.Object,
                RoleRepositoryMock.Object,
                RoleMapperMock.Object,
                CurrentUserServiceMock.Object,
                AuditLoggerMock.Object);
        }
    }
}
