using FluentAssertions;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompanyById;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Queries.GetRoleCompanyById;

/// <summary>
/// Tests for the single RoleCompany lookup handler.
/// Covers tenant validation, hit/miss branches, and tenant-scoped DTO forwarding.
/// </summary>
public sealed class GetRoleCompanyByIdQueryHandlerTests
{
    /// <summary>
    /// Empty CompanyId short-circuits with the canonical 401 code, never calling the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompanyByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
        context.RoleCompanyRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Repo returns a DTO: handler responds OK carrying that DTO.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLinkExists_ShouldReturnOkWithDto()
    {
        var tenantId = Guid.NewGuid();
        var id = Guid.NewGuid();
        var dto = new RoleCompanyDto
        {
            Id = id,
            RoleId = Guid.NewGuid(),
            RoleName = "Admin",
            IsSystemDefault = true,
            CreatedBy = "user-1",
            Created = DateTime.UtcNow
        };

        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdAsync(id, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompanyByIdQuery(id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(dto);
    }

    /// <summary>
    /// Repo returns null: handler responds with the canonical 404 code.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLinkMissing_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCompanyDto?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompanyByIdQuery(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_COMPANY_NOT_FOUND");
    }

    private sealed class TestContext
    {
        public Mock<IRoleCompanyRepository> RoleCompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRoleCompanyByIdQueryHandler CreateHandler() =>
            new(RoleCompanyRepositoryMock.Object, CurrentUserServiceMock.Object);
    }
}
