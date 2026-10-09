using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Security.SystemOptions;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions;

/// <summary>
/// Unit tests for <see cref="SystemOptionCascadeCoordinator"/> (SPEC 41, decision 2026-10-08).
/// </summary>
public sealed class SystemOptionCascadeCoordinatorTests
{
    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly DateTime CascadeDay = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly int Stamp = BaseAuditableEntity.GetDeletionGcRecordStamp(CascadeDay);

    [Fact]
    public async Task GetActiveSubtreeAsync_ShouldWalkEveryLevelThroughActiveOptions()
    {
        var root = Option();
        var child = Option(root.Id);
        var grandChild = Option(child.Id);
        var deletedChild = Option(root.Id, deletedOn: CascadeDay);
        var underDeleted = Option(deletedChild.Id);
        var other = Option();
        var coordinator = CreateCoordinator(root, child, grandChild, deletedChild, underDeleted, other);

        var subtree = await coordinator.GetActiveSubtreeAsync(ModuleId, root.Id);
        var wholeModule = await coordinator.GetActiveSubtreeAsync(ModuleId, rootOptionId: null);

        subtree.Should().BeEquivalentTo(new[] { child, grandChild });
        wholeModule.Should().HaveCount(5, "every active option of the module");
    }

    [Fact]
    public async Task RestoreSubtreeAsync_ForAnOption_ShouldRestoreOnlyDescendantsOfTheSameCascade()
    {
        var root = Option(deletedOn: CascadeDay);
        var child = Option(root.Id, deletedOn: CascadeDay);
        var grandChild = Option(child.Id, deletedOn: CascadeDay);
        var deletedBefore = Option(root.Id, deletedOn: CascadeDay.AddDays(-10));
        var coordinator = CreateCoordinator(root, child, grandChild, deletedBefore);

        var error = await coordinator.RestoreSubtreeAsync(ModuleId, root.Id, Stamp);

        error.Should().BeNull();
        child.IsDeleted.Should().BeFalse();
        grandChild.IsDeleted.Should().BeFalse();
        deletedBefore.IsDeleted.Should().BeTrue();
        root.IsDeleted.Should().BeTrue("the root itself is restored by SoftDeleteRestorer, not by the coordinator");
    }

    [Fact]
    public async Task RestoreSubtreeAsync_ForAModule_ShouldNotRestoreAnOptionUnderAParentOfAnotherCascade()
    {
        var parentDeletedBefore = Option(deletedOn: CascadeDay.AddDays(-10));
        var orphan = Option(parentDeletedBefore.Id, deletedOn: CascadeDay);
        var topLevel = Option(deletedOn: CascadeDay);
        var nested = Option(topLevel.Id, deletedOn: CascadeDay);
        var coordinator = CreateCoordinator(parentDeletedBefore, orphan, topLevel, nested);

        await coordinator.RestoreSubtreeAsync(ModuleId, rootOptionId: null, Stamp);

        topLevel.IsDeleted.Should().BeFalse();
        nested.IsDeleted.Should().BeFalse();
        orphan.IsDeleted.Should().BeTrue("a child never comes back under a deleted parent");
        parentDeletedBefore.IsDeleted.Should().BeTrue();
    }

    private static SystemOption Option(Guid? parentId = null, DateTime? deletedOn = null)
    {
        var option = new SystemOption { ModuleId = ModuleId, Name = "Option", Route = $"/{Guid.NewGuid():N}", ParentId = parentId };
        if (deletedOn is { } day)
        {
            option.MarkAsDeleted(day);
        }

        return option;
    }

    private static SystemOptionCascadeCoordinator CreateCoordinator(params SystemOption[] options)
    {
        var unitOfWorkMock = new Mock<IUnitOfWork>();
        unitOfWorkMock.SetupRepositoryRows<SystemOption>(options);
        return new SystemOptionCascadeCoordinator(unitOfWorkMock.Object);
    }
}
