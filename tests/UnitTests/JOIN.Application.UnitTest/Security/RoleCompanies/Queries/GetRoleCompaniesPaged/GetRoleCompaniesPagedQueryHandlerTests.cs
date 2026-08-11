using FluentAssertions;
using JOIN.Application.DTO.Security.RoleCompany;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Queries.GetRoleCompaniesPaged;

/// <summary>
/// Tests for the paged RoleCompany listing handler. Validates tenant scoping,
/// page/pageSize clamping, and that filters are passed through to the repository.
/// </summary>
public sealed class GetRoleCompaniesPagedQueryHandlerTests
{
    /// <summary>
    /// Empty CompanyId short-circuits with 401.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(null, null), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    /// <summary>
    /// No filters → repo receives (tenantId, null, null, 1, 20).
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoFilters_ShouldPassDefaultsToRepo()
    {
        var tenantId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetPagedAsync(tenantId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleCompanyListItemDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(null, null), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().BeEmpty();
        response.Data.TotalCount.Should().Be(0);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(20);
    }

    /// <summary>
    /// pageSize above 100 clamps to 100.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeAboveMax_ShouldClampToHundred()
    {
        var tenantId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetPagedAsync(tenantId, null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleCompanyListItemDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(null, null, Page: 1, PageSize: 250), CancellationToken.None);

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
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetPagedAsync(tenantId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleCompanyListItemDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(null, null, Page: -5, PageSize: 20), CancellationToken.None);

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
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetPagedAsync(tenantId, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleCompanyListItemDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(null, null, Page: 1, PageSize: 0), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(20);
    }

    /// <summary>
    /// Optional filters are forwarded to the repository verbatim.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFiltersProvided_ShouldForwardToRepo()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetPagedAsync(tenantId, roleId, true, 2, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<RoleCompanyListItemDto>(), 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRoleCompaniesPagedQuery(roleId, true, 2, 50), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageNumber.Should().Be(2);
        response.Data.PageSize.Should().Be(50);
    }

    private sealed class TestContext
    {
        public Mock<IRoleCompanyRepository> RoleCompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRoleCompaniesPagedQueryHandler CreateHandler() =>
            new(RoleCompanyRepositoryMock.Object, CurrentUserServiceMock.Object);
    }
}
