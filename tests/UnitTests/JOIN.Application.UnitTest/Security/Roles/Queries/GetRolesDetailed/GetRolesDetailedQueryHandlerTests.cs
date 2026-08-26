using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Roles.Queries.GetRolesDetailed;
using Moq;

namespace JOIN.Application.UnitTest.Security.Roles.Queries.GetRolesDetailed;

/// <summary>
/// Verifies the page/pageSize sanitization rules on the detailed roles listing handler,
/// and that the caller's CompanyId is forwarded to the repository for tenant-scoped PermissionsCount.
/// </summary>
public sealed class GetRolesDetailedQueryHandlerTests
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
        var response = await handler.Handle(new GetRolesDetailedQuery("admin", true), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// Repository is invoked with the raw filters, the sanitized page/pageSize, and the caller's CompanyId.
    /// </summary>
    [Fact]
    public async Task Handle_WithDefaultPagination_ShouldCallRepositoryWithClampedValues()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        var items = new List<RoleDto> { BuildRole("Admin") };
        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync("admin", true, 1, 20, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<RoleDto>)items, 1));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRolesDetailedQuery("admin", true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().HaveCount(1);
        response.Data.PageNumber.Should().Be(1);
        response.Data.PageSize.Should().Be(20);
    }

    /// <summary>
    /// pageSize above 100 is clamped down to 100 before the repository is called.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeIsAboveMax_ShouldClampToOneHundred()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        IReadOnlyList<RoleDto>? capturedItems = null;
        int capturedPageSize = 0;
        int capturedPage = 0;

        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync(null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, bool?, int, int, Guid, CancellationToken>((_, _, p, ps, _, _) =>
            {
                capturedPage = p;
                capturedPageSize = ps;
                capturedItems = Array.Empty<RoleDto>();
            })
            .ReturnsAsync(() => (capturedItems!, 0));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRolesDetailedQuery(null, null, Page: 1, PageSize: 250), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        capturedPageSize.Should().Be(100);
        capturedPage.Should().Be(1);
    }

    /// <summary>
    /// page below 1 is bumped to 1 before hitting the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageIsBelowOne_ShouldClampToOne()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        int capturedPage = -1;

        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync(null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, bool?, int, int, Guid, CancellationToken>((_, _, p, _, _, _) => capturedPage = p)
            .ReturnsAsync((Array.Empty<RoleDto>(), 0));

        var handler = context.CreateHandler();
        await handler.Handle(new GetRolesDetailedQuery(null, null, Page: 0, PageSize: 10), CancellationToken.None);

        capturedPage.Should().Be(1);
    }

    /// <summary>
    /// pageSize below 1 falls back to the default page size (20).
    /// </summary>
    [Fact]
    public async Task Handle_WhenPageSizeIsZeroOrNegative_ShouldFallBackToDefault()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        int capturedPageSize = -1;

        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync(null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, bool?, int, int, Guid, CancellationToken>((_, _, _, ps, _, _) => capturedPageSize = ps)
            .ReturnsAsync((Array.Empty<RoleDto>(), 0));

        var handler = context.CreateHandler();
        await handler.Handle(new GetRolesDetailedQuery(null, null, Page: 1, PageSize: 0), CancellationToken.None);

        capturedPageSize.Should().Be(20);
    }

    /// <summary>
    /// The case-preserved name filter is forwarded verbatim to the repository.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNameProvided_ShouldForwardAsIsToRepository()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        string? capturedName = null;

        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync(It.IsAny<string?>(), It.IsAny<bool?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string?, bool?, int, int, Guid, CancellationToken>((n, _, _, _, _, _) => capturedName = n)
            .ReturnsAsync((Array.Empty<RoleDto>(), 0));

        var handler = context.CreateHandler();
        await handler.Handle(new GetRolesDetailedQuery("Admin", null), CancellationToken.None);

        capturedName.Should().Be("Admin");
    }

    /// <summary>
    /// TotalPages is computed from TotalCount and the sanitized pageSize.
    /// </summary>
    [Fact]
    public async Task Handle_WhenTotalCountIsNonZero_ShouldComputeTotalPages()
    {
        var companyId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        context.RoleRepositoryMock
            .Setup(x => x.GetPagedAsync(null, null, 1, 20, companyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<RoleDto>)new List<RoleDto>(), 55));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetRolesDetailedQuery(null, null), CancellationToken.None);

        response.Data!.TotalCount.Should().Be(55);
        response.Data.TotalPages.Should().Be(3);
    }

    private RoleDto BuildRole(string name) => new(
        Guid.NewGuid(),
        name,
        name.ToUpperInvariant(),
        null,
        false,
        "seed",
        DateTime.UtcNow,
        PermissionsCount: 0);

    private sealed class TestContext
    {
        public Mock<IRoleRepository> RoleRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetRolesDetailedQueryHandler CreateHandler()
            => new(RoleRepositoryMock.Object, CurrentUserServiceMock.Object);
    }
}
