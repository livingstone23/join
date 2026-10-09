using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Commands.RestoreIncomeRange;

/// <summary>
/// Unit tests for <see cref="RestoreIncomeRangeCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestoreIncomeRangeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = IncomeRange.Create(CompanyId, "Medium", 1000m, 2000m, "USD", 1);
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDuplicateIsAlsoDeleted_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = IncomeRange.Create(CompanyId, "Medium", 1000m, 2000m, "USD", 1);
        duplicate.MarkAsDeleted();
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<IncomeRange>()).Returns(EntityRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<IncomeRange>> EntityRepositoryMock { get; } = new();

        public IncomeRange SetupDeleted()
        {
            var entity = IncomeRange.Create(CompanyId, "Low", 0m, 1000m, "USD", 1);
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestoreIncomeRangeCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
