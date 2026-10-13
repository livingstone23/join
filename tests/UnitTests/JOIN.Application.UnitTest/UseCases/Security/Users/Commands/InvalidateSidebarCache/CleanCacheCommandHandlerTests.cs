using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// SPEC 51-post — unit tests for the canonical <see cref="CleanCacheCommandHandler"/>.
/// Each <c>cacheKey</c> branches through <see cref="IPermissionService.InvalidateUserCacheAsync"/>
/// (and a direct <see cref="IMemoryCache.Remove"/> for the sidebar-only branch) so the
/// permission-snapshot key the handler touches always matches the one
/// <c>PermissionService</c> actually stores (<c>permissions:v2:{companyId}:{userId}</c>).
/// </summary>
public sealed class CleanCacheCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly string SidebarKey = $"sidebar:{CompanyId}:{UserId}";
    private static readonly string PermissionKey = $"permissions:v2:{CompanyId}:{UserId}";

    [Fact]
    public async Task Handle_WhenCacheKeyIsSidebar_ShouldRemoveOnlySidebarEntry()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(SidebarKey, "menu");
        cache.Set(PermissionKey, "snapshot");
        var permissions = new Mock<IPermissionService>(MockBehavior.Strict);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "sidebar"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Cache 'sidebar' cleaned successfully.");
        cache.TryGetValue(SidebarKey, out _).Should().BeFalse();
        cache.TryGetValue(PermissionKey, out _).Should().BeTrue("'sidebar' only clears the menu cache");
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenCacheKeyIsPermission_ShouldInvalidateUserCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var permissions = new Mock<IPermissionService>();
        permissions
            .Setup(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "permission"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Cache 'permission' cleaned successfully.");
        permissions.Verify(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheKeyIsPermissions_ShouldInvalidateUserCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var permissions = new Mock<IPermissionService>();
        permissions
            .Setup(p => p.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "permissions"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Cache 'permissions' cleaned successfully.");
        permissions.Verify(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheKeyIsAll_ShouldInvalidateUserCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set(SidebarKey, "menu");
        var permissions = new Mock<IPermissionService>();
        permissions
            .Setup(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "all"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Cache 'all' cleaned successfully.");
        cache.TryGetValue(SidebarKey, out _).Should().BeFalse();
        permissions.Verify(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheKeyIsUnsupported_ShouldReturnInvalidCacheKeyWithoutTouchingPermissionService()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var permissions = new Mock<IPermissionService>(MockBehavior.Strict);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "audit-log"
        }, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_CACHE_KEY");
        permissions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenCacheKeyHasSurroundingWhitespaceAndCase_ShouldNormalizeBeforeRouting()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var permissions = new Mock<IPermissionService>();
        permissions
            .Setup(p => p.InvalidateUserCacheAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CleanCacheCommandHandler(cache, permissions.Object);
        var response = await handler.Handle(new CleanCacheCommand
        {
            UserId = UserId,
            CompanyId = CompanyId,
            CacheKey = "  PERMISSION  "
        }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Cache 'permission' cleaned successfully.");
        permissions.Verify(p => p.InvalidateUserCacheAsync(CompanyId, UserId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
