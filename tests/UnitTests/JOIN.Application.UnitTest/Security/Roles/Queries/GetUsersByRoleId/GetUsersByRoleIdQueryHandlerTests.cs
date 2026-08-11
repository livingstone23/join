using FluentAssertions;
using JOIN.Application.DTO.Security.RoleUsers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Roles.Queries.GetUsersByRoleId;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Queries.GetUsersByRoleId;

/// <summary>
/// Tests for the "usuarios afectados por rol" preview handler. Validates tenant scoping,
/// role existence pre-check, page/pageSize clamping, and that the projection is returned verbatim.
/// </summary>
public sealed class GetUsersByRoleIdQueryHandlerTests
{
    /// <summary>
    /// Empty CompanyId short-circuits with 401 INVALID_COMPANY_ID before any DB call.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.RoleRepositoryMock.Verify(
            x => x.ExistsAndActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.RoleCompanyRepositoryMock.Verify(
            x => x.GetUsersByRoleIdPagedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Missing or soft-deleted role short-circuits with 404 before any join query.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRoleMissing_ShouldReturnRoleNotFound()
    {
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("Rol no encontrado o inactivo.");
        context.RoleCompanyRepositoryMock.Verify(
            x => x.GetUsersByRoleIdPagedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Happy path: role exists, defaults flow through, paged DTOs returned.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHappyPath_ShouldReturnPaged()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var seed = new RoleAffectedUserDto
        {
            Id = Guid.NewGuid(),
            FullName = "Ada Lovelace",
            IsActive = true,
            UserName = "ada@join.com",
            Email = "ada@join.com",
            PhoneNumber = "+1-555-0100",
            Created = DateTime.UtcNow,
            IsSuperAdmin = false,
            IsSuperAdminCompany = false,
            EmailConfirmed = true
        };

        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetUsersByRoleIdPagedAsync(roleId, tenantId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleAffectedUserDto> { seed }, 1));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.Items.First().Should().BeEquivalentTo(seed);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(20);
        response.Data.TotalCount.Should().Be(1);
        response.Data.TotalPages.Should().Be(1);
    }

    /// <summary>
    /// pageSize above 100 clamps to 100.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeAboveMax_ShouldClampToHundred()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetUsersByRoleIdPagedAsync(roleId, tenantId, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleAffectedUserDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId, Page: 1, PageSize: 250), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(100);
    }

    /// <summary>
    /// page below 1 clamps to 1.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageBelowOne_ShouldClampToOne()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetUsersByRoleIdPagedAsync(roleId, tenantId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleAffectedUserDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId, Page: -5, PageSize: 20), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageNumber.Should().Be(1);
    }

    /// <summary>
    /// pageSize below 1 falls back to the default (20).
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeBelowOne_ShouldFallbackToDefault()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetUsersByRoleIdPagedAsync(roleId, tenantId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleAffectedUserDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId, Page: 1, PageSize: 0), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(20);
    }

    /// <summary>
    /// Empty result still returns IsSuccess=true with zeroed counters.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoUsersAssigned_ShouldReturnEmptyPagedResult()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleRepositoryMock
            .Setup(x => x.ExistsAndActiveAsync(roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetUsersByRoleIdPagedAsync(roleId, tenantId, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleAffectedUserDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetUsersByRoleIdQuery(roleId), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
        response.Data.TotalPages.Should().Be(0);
    }

    private sealed class TestContext
    {
        public Mock<IRoleCompanyRepository> RoleCompanyRepositoryMock { get; } = new();
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetUsersByRoleIdQueryHandler CreateHandler() =>
            new(RoleCompanyRepositoryMock.Object, RoleRepositoryMock.Object, CurrentUserServiceMock.Object);
    }
}
