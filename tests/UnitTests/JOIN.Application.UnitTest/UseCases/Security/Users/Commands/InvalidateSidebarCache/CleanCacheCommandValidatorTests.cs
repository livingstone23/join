using FluentAssertions;
using JOIN.Application.UseCases.Security.Users.Commands.InvalidateSidebarCache;

namespace JOIN.Application.UnitTest.UseCases.Security.Users.Commands.InvalidateSidebarCache;

/// <summary>
/// Contains the unit tests for the cache invalidation command validator.
/// </summary>
public sealed class CleanCacheCommandValidatorTests
{
    [Theory]
    [InlineData("sidebar", null)]
    [InlineData(" Permission ", null)]
    [InlineData("permissions", null)]
    [InlineData(null, "ALL")]
    public void Validate_WhenTargetKeyIsSupported_ShouldPass(string? cacheKey, string? cleanCache)
        => new CleanCacheCommandValidator()
            .Validate(new CleanCacheCommand { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CacheKey = cacheKey, CleanCache = cleanCache })
            .IsValid.Should().BeTrue();

    [Fact]
    public void Validate_WhenFieldsAreMissing_ShouldFailEveryRule()
        => new CleanCacheCommandValidator()
            .Validate(new CleanCacheCommand())
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["UserId", "CompanyId", "TargetKey"]);

    [Fact]
    public void Validate_WhenTargetKeyIsUnsupported_ShouldFail()
        => new CleanCacheCommandValidator()
            .Validate(new CleanCacheCommand { UserId = Guid.NewGuid(), CompanyId = Guid.NewGuid(), CacheKey = "menus" })
            .Errors.Should().ContainSingle(e => e.PropertyName == "TargetKey");
}
