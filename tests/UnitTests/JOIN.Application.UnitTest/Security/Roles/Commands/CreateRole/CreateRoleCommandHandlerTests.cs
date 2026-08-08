using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Commands.CreateRole;

/// <summary>
/// Tests for the role creation command handler.
/// Covers tenant validation, duplicate name detection, persistence failure, and the happy path.
/// </summary>
public sealed class CreateRoleCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Empty CompanyId short-circuits before the repository is touched.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnErrorWithoutTouchingRepo()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        context.RoleRepositoryMock.Verify(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Duplicate name returns the canonical conflict message and skips AddAsync.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameAlreadyExists_ShouldReturnConflictWithoutInsert()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.ExistsByNameAsync("ADMIN", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().StartWith("Ya existe un rol con el nombre");
        context.RoleRepositoryMock.Verify(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()), Times.Never);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 produces an error response.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnError()
    {
        var context = new TestContext();
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new CreateRoleCommand("Admin", null, false), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Happy path: the role is created and the returned DTO carries the trimmed/normalized values.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRequestIsValid_ShouldCreateRoleAndReturnDto()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleRepositoryMock.Setup(x => x.ExistsByNameAsync("ADMIN", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        ApplicationRole? captured = null;
        context.RoleRepositoryMock
            .Setup(x => x.AddAsync(It.IsAny<ApplicationRole>(), It.IsAny<CancellationToken>()))
            .Callback<ApplicationRole, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var mapperMock = new Mock<IRoleMapper>();
        var dto = new RoleDto(Guid.NewGuid(), "Admin", "ADMIN", "All access", false, "user-1", DateTime.UtcNow);
        mapperMock.Setup(x => x.FromEntity(It.IsAny<ApplicationRole>())).Returns(dto);

        var handler = new CreateRoleCommandHandler(
            context.UnitOfWorkMock.Object,
            context.RoleRepositoryMock.Object,
            mapperMock.Object,
            context.CurrentUserServiceMock.Object);

        var response = await handler.Handle(new CreateRoleCommand("  Admin  ", "All access", false), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Admin");
        captured.NormalizedName.Should().Be("ADMIN");
        captured.Description.Should().Be("All access");
        captured.CreatedBy.Should().Be("user-1");
        response.Data.Should().Be(dto);
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public CreateRoleCommandHandler CreateHandler()
        {
            var mapperMock = new Mock<IRoleMapper>();
            return new CreateRoleCommandHandler(
                UnitOfWorkMock.Object,
                RoleRepositoryMock.Object,
                mapperMock.Object,
                CurrentUserServiceMock.Object);
        }
    }
}
