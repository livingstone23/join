using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Infrastructure.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace JOIN.Application.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="PermissionService"/> covering the seven permission flags
/// (CanRead / CanCreate / CanUpdate / CanDelete / CanDownload / CanExport / CanExecute),
/// the explicit flag override, HTTP verb fallback, cache hit/miss, and OR-merge across roles.
/// </summary>
public sealed class PermissionServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherRoleId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SystemOptionId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>
    /// Reflectively sets the protected <see cref="BaseEntity.Id"/> on a freshly-constructed entity.
    /// Required because the InMemory provider relies on the FK being set BEFORE <c>SaveChanges</c>
    /// resolves the navigation property.
    /// </summary>
    private static T WithId<T>(T entity, Guid id) where T : BaseEntity
    {
        typeof(BaseEntity)
            .GetProperty(nameof(BaseEntity.Id))!
            .SetValue(entity, id);
        return entity;
    }

    /// <summary>
    /// Builds an InMemory <see cref="ApplicationDbContext"/> seeded with a single user-role-company link
    /// and the supplied <see cref="RoleSystemOption"/> rows.
    /// </summary>
    private static ApplicationDbContext CreateContext(params RoleSystemOption[] roleOptions)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"permission-tests-{Guid.NewGuid()}")
            .Options;

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(c => c.UserId).Returns(UserId.ToString());
        currentUser.Setup(c => c.CompanyId).Returns(CompanyId);

        var interceptor = new AuditableEntitySaveChangesInterceptor(currentUser.Object);

        var ctx = new ApplicationDbContext(options, interceptor, currentUser.Object);

        ctx.UserRoleCompanies.Add(WithId(new UserRoleCompany
        {
            UserId = UserId,
            RoleId = RoleId,
            CompanyId = CompanyId,
            GcRecord = 0
        }, Guid.NewGuid()));

        ctx.SystemOptions.Add(WithId(new SystemOption
        {
            Name = "Persons",
            ControllerName = "Persons",
            Route = "/admin/persons",
            GcRecord = 0
        }, SystemOptionId));

        foreach (var roleOption in roleOptions)
        {
            roleOption.CompanyId = CompanyId;
            roleOption.SystemOptionId = SystemOptionId;
            roleOption.GcRecord = 0;
            ctx.RoleSystemOptions.Add(roleOption);
        }

        ctx.SaveChanges();
        return ctx;
    }

    private static PermissionService CreateService(ApplicationDbContext ctx) =>
        new(ctx, new MemoryCache(new MemoryCacheOptions()));

    private static RoleSystemOption BuildRoleOption(
        Guid roleId,
        bool canRead = false,
        bool canCreate = false,
        bool canUpdate = false,
        bool canDelete = false,
        bool canDownload = false,
        bool canExport = false,
        bool canExecute = false) =>
        new()
        {
            RoleId = roleId,
            SystemOptionId = SystemOptionId,
            CompanyId = CompanyId,
            GcRecord = 0,
            CanRead = canRead,
            CanCreate = canCreate,
            CanUpdate = canUpdate,
            CanDelete = canDelete,
            CanDownload = canDownload,
            CanExport = canExport,
            CanExecute = canExecute
        };

    [Fact]
    public async Task HasPermissionAsync_WhenGuidIsInvalid_ShouldReturnFalse()
    {
        var ctx = CreateContext();
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync("not-a-guid", CompanyId.ToString(), "Persons", "GET");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenUserHasNoRoles_ShouldReturnFalse()
    {
        var ctx = CreateContext();
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "GET");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsGetAndCanReadIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "GET");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsPostAndCanCreateIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canCreate: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "POST");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsPutAndCanUpdateIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canUpdate: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "PUT");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsPatchAndCanUpdateIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canUpdate: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "PATCH");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsDeleteAndCanDeleteIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canDelete: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "DELETE");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsHeadAndCanReadIsTrue_ShouldReturnTrue()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "HEAD");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenVerbIsUnsupported_ShouldReturnFalse()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true, canCreate: true, canUpdate: true, canDelete: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "OPTIONS");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithExplicitFlagCanExportOnGet_ShouldEvaluateCanExport()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true, canExport: false));
        var service = CreateService(ctx);

        var withoutExport = await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET", PermissionFlags.CanExport);
        withoutExport.Should().BeFalse();

        var ctxWithExport = CreateContext(BuildRoleOption(RoleId, canRead: true, canExport: true));
        var serviceWithExport = CreateService(ctxWithExport);

        var withExport = await serviceWithExport.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET", PermissionFlags.CanExport);
        withExport.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithExplicitFlagCanExecuteOnPost_ShouldEvaluateCanExecute()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canExecute: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "POST", PermissionFlags.CanExecute);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithExplicitFlagCanDownloadOnGet_ShouldEvaluateCanDownload()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canDownload: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET", PermissionFlags.CanDownload);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenRolesConflict_ShouldOrMergeFlags()
    {
        // Note: only one role is linked to the user in the seeded context (RoleId).
        // Build a second link so both roles are evaluated.
        var ctx = CreateContext(
            BuildRoleOption(RoleId, canRead: true, canExport: false),
            BuildRoleOption(OtherRoleId, canRead: false, canExport: true));

        ctx.UserRoleCompanies.Add(WithId(new UserRoleCompany
        {
            UserId = UserId,
            RoleId = OtherRoleId,
            CompanyId = CompanyId,
            GcRecord = 0
        }, Guid.NewGuid()));
        ctx.SaveChanges();

        var service = CreateService(ctx);

        var exportResult = await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET", PermissionFlags.CanExport);
        var readResult = await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET");

        exportResult.Should().BeTrue();
        readResult.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_SecondCallUsesCacheAndReturnsSameResult()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true));
        var service = CreateService(ctx);

        var first = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "GET");
        var second = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "GET");

        first.Should().BeTrue();
        second.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenResourceNameIsNormalizedSingular_ShouldMatch()
    {
        // Seeded SystemOption.ControllerName = "Persons" (plural).
        var ctx = CreateContext(BuildRoleOption(RoleId, canRead: true));
        var service = CreateService(ctx);

        var result = await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Person", "GET");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WhenAllFlagsFalse_ShouldReturnFalseForAnyVerb()
    {
        var ctx = CreateContext(BuildRoleOption(RoleId));
        var service = CreateService(ctx);

        (await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "GET")).Should().BeFalse();
        (await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "POST")).Should().BeFalse();
        (await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "PUT")).Should().BeFalse();
        (await service.HasPermissionAsync(UserId.ToString(), CompanyId.ToString(), "Persons", "DELETE")).Should().BeFalse();
        (await service.HasPermissionAsync(
            UserId.ToString(), CompanyId.ToString(), "Persons", "GET", PermissionFlags.CanExport)).Should().BeFalse();
    }
}
