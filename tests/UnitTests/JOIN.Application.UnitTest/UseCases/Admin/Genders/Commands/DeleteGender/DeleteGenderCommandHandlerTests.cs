using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Genders.Commands;
using JOIN.Domain.Admin;
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
        context.PersonRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new Person { CompanyId = CompanyId, GenderId = entity.Id }]);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("GENDER_IN_USE");
        context.GenderRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<Gender>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(new DeleteGenderCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenGenderIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.PersonRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([new Person { CompanyId = Guid.NewGuid(), GenderId = entity.Id }]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
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
