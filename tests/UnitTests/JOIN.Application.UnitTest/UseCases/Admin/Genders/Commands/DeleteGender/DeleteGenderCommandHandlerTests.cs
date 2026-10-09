using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Genders.Commands;
using JOIN.Domain.Admin;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Audit;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Commands.DeleteGender;

/// <summary>
/// Contains the unit tests for the gender soft-delete command handler.
/// </summary>
public sealed class DeleteGenderCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenGenderIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("GENDER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenGenderIsAssignedToPerson_ShouldReturnInUse()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.SetupRows([new Person { CompanyId = CompanyId, GenderId = entity.Id }]);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("GENDER_IN_USE");
        context.GenderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Gender>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.SetupRows([]);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenGenderIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.SetupRows([new Person { CompanyId = CompanyId, GenderId = entity.Id, GcRecord = 20260101 }]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    /// <summary>
    /// SPEC 41: every active child type blocks the delete and is listed in the errors.
    /// </summary>
    [Fact]
    public async Task Handle_WhenActiveDependentsExist_ShouldReturnInUseWithDetails()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.SetupRows([new Person { CompanyId = CompanyId, GenderId = entity.Id, GcRecord = 20260101 }]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Person { CompanyId = Guid.NewGuid(), GenderId = entity.Id };
        context.UnitOfWorkMock.SetupRepositoryRows<Person>([child1]);


        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("GENDER_IN_USE");
        response.Errors.Should().Equal("Active persons: 1");
        entity.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// SPEC 41: logically deleted children do not block the delete.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDependentsAreDeleted_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.SetupRows([new Person { CompanyId = CompanyId, GenderId = entity.Id, GcRecord = 20260101 }]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = new Person { CompanyId = Guid.NewGuid(), GenderId = entity.Id };
        child1.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<Person>([child1]);


        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Gender>()).Returns(GenderRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Person>()).Returns(PersonRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Gender>> GenderRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Person>> PersonRepositoryMock { get; } = new();

        public Gender SetupExisting()
        {
            var entity = Gender.Create(CompanyId, "M", "Masculino");
            GenderRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteGenderCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
