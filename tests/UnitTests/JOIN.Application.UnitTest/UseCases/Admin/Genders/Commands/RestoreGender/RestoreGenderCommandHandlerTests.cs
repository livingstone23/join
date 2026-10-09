using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.Genders.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Commands.RestoreGender;

/// <summary>
/// Unit tests for <see cref="RestoreGenderCommandHandler"/> (SPEC 41).
/// </summary>
public sealed class RestoreGenderCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(companyId: Guid.Empty);

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotSuperAdmin_ShouldReturnSuperAdminRequired()
    {
        var context = new TestContext(isSuperAdmin: false);
        var entity = context.SetupDeleted();

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("SUPERADMIN_REQUIRED");
        entity.IsDeleted.Should().BeTrue();
        context.EntityRepositoryMock.Verify(x => x.GetIncludingDeletedAsync(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        entity.Restore();

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("NOT_DELETED");
    }

    [Fact]
    public async Task Handle_WhenChecksPass_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = Gender.Create(CompanyId, "M", "Masculine");
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        entity.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenDuplicateIsAlsoDeleted_ShouldRestore()
    {
        var context = new TestContext();
        var entity = context.SetupDeleted();
        var duplicate = Gender.Create(CompanyId, "M", "Masculine");
        duplicate.MarkAsDeleted();
        context.EntityRepositoryMock.SetupRows([entity, duplicate]);
        context.SetupSaveChanges(1);

        var response = await context.CreateHandler().Handle(new RestoreGenderCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDeleted.Should().BeFalse();
    }

    private sealed class TestContext
    {
        public TestContext(Guid? companyId = null, bool isSuperAdmin = true)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId ?? CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<Gender>()).Returns(EntityRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Gender>> EntityRepositoryMock { get; } = new();

        public Gender SetupDeleted()
        {
            var entity = Gender.Create(CompanyId, "M", "Male");
            entity.MarkAsDeleted();
            EntityRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(entity.Id)).ReturnsAsync(entity);
            EntityRepositoryMock.SetupRows([entity]);
            return entity;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public RestoreGenderCommandHandler CreateHandler()
            => new(CurrentUserServiceMock.Object, new SoftDeleteRestorer(UnitOfWorkMock.Object, CurrentUserServiceMock.Object));
    }
}
