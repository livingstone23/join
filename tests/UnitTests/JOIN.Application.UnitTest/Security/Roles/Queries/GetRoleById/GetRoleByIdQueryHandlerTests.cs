using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRoleById;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Queries.GetRoleById;

/// <summary>
/// Tests for the single-role lookup handler.
/// </summary>
public sealed class GetRoleByIdQueryHandlerTests
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
        var response = await handler.Handle(new GetRoleByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// When the repository returns a DTO, the handler responds with a successful response carrying that DTO.
    /// The repository call must include the caller's CompanyId so PermissionsCount stays tenant-scoped.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleExists_ShouldReturnOkWithDto()
    {
        var companyId = Guid.NewGuid();
        var role = new RoleDto(Guid.NewGuid(), "Admin", "ADMIN", "System admin", true, "seed", DateTime.UtcNow, PermissionsCount: 5);
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(role.Id, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(role);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleByIdQuery(role.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(role);
        response.Data!.PermissionsCount.Should().Be(5);
    }

    /// <summary>
    /// When the repository returns null, the handler responds with the canonical 404 message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleMissing_ShouldReturnNotFoundMessage()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleDto?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Rol no encontrado o inactivo.");
    }

    private sealed class TestContext
    {
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRoleByIdQueryHandler CreateHandler()
            => new(RoleRepositoryMock.Object, CurrentUserServiceMock.Object);
    }
}