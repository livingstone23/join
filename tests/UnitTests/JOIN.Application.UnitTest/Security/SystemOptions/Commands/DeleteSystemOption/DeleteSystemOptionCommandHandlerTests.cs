using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Security.SystemOptions;
using JOIN.Application.UseCases.Security.SystemOptions.Commands;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Security.SystemOptions.Commands.DeleteSystemOption;

/// <summary>
/// Unit tests for <see cref="DeleteSystemOptionCommandHandler"/>. SPEC 41: active child options and
/// active role assignments block the logical delete and are listed in the errors.
/// </summary>
public sealed class DeleteSystemOptionCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenOptionDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_OPTION_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenActiveDependentsExist_ShouldReturnInUseWithDetails()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        context.OptionRepositoryMock.SetupRows([entity, NewOption(parentId: entity.Id)]);
        context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([new RoleSystemOption { RoleId = Guid.NewGuid(), SystemOptionId = entity.Id }]);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SYSTEM_OPTION_IN_USE");
        response.Errors.Should().Equal("Active role system options: 1");
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    [Fact]
    public async Task Handle_WhenOnlyRoleAssignmentIsActive_ShouldReturnInUse()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        context.OptionRepositoryMock.SetupRows([entity]);
        context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([new RoleSystemOption { RoleId = Guid.NewGuid(), SystemOptionId = entity.Id }]);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SYSTEM_OPTION_IN_USE");
        response.Errors.Should().Equal("Active role system options: 1");
    }

    [Fact]
    public async Task Handle_WhenDependentsAreDeleted_ShouldSoftDelete()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        var deletedChild = NewOption(parentId: entity.Id);
        deletedChild.MarkAsDeleted();
        var deletedAssignment = new RoleSystemOption { RoleId = Guid.NewGuid(), SystemOptionId = entity.Id };
        deletedAssignment.MarkAsDeleted();
        context.OptionRepositoryMock.SetupRows([entity, deletedChild]);
        context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([deletedAssignment]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        context.OptionRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenADescendantIsGrantedToARole_ShouldReturnInUse()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        var child = NewOption(parentId: entity.Id);
        child.ModuleId = entity.ModuleId;
        var grandChild = NewOption(parentId: child.Id);
        grandChild.ModuleId = entity.ModuleId;
        context.OptionRepositoryMock.SetupRows([entity, child, grandChild]);
        context.UnitOfWorkMock.SetupRepositoryRows<RoleSystemOption>([new RoleSystemOption { RoleId = Guid.NewGuid(), SystemOptionId = grandChild.Id }]);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SYSTEM_OPTION_IN_USE");
        response.Errors.Should().Equal("Active role system options: 1");
    }

    [Fact]
    public async Task Handle_WhenOptionHasChildren_ShouldCascadeToEveryLevel()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        var child = NewOption(parentId: entity.Id);
        child.ModuleId = entity.ModuleId;
        var grandChild = NewOption(parentId: child.Id);
        grandChild.ModuleId = entity.ModuleId;
        var sibling = NewOption();
        sibling.ModuleId = entity.ModuleId;
        context.OptionRepositoryMock.SetupRows([entity, child, grandChild, sibling]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        new[] { child.GcRecord, grandChild.GcRecord }.Should().OnlyContain(g => g == entity.GcRecord && g > 0);
        sibling.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord, "only the subtree is deleted");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext();
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await context.CreateHandler().Handle(new DeleteSystemOptionCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    private static SystemOption NewOption(Guid? parentId = null) => new()
    {
        ModuleId = Guid.NewGuid(),
        Name = "Persons",
        Route = $"/persons-{Guid.NewGuid():N}",
        ParentId = parentId
    };

    private sealed class TestContext
    {
        public TestContext()
        {
            UnitOfWorkMock.Setup(x => x.GetRepository<SystemOption>()).Returns(OptionRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<IGenericRepository<SystemOption>> OptionRepositoryMock { get; } = new();

        public SystemOption SetupExisting()
        {
            var entity = NewOption();
            OptionRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteSystemOptionCommandHandler CreateHandler() => new(UnitOfWorkMock.Object, new SystemOptionCascadeCoordinator(UnitOfWorkMock.Object));
    }
}
