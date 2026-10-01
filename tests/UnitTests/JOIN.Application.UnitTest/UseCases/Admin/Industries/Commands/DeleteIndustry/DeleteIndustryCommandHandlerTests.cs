using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Industries.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Industries.Commands.DeleteIndustry;

/// <summary>
/// Contains the unit tests for the industry soft-delete command handler.
/// </summary>
public sealed class DeleteIndustryCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new DeleteIndustryCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenIndustryIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(new DeleteIndustryCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenIndustryIsUsedByBusinessProfile_ShouldReturnInUse()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), entity.Id, Guid.NewGuid())]);

        var response = await context.CreateHandler().Handle(new DeleteIndustryCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(new DeleteIndustryCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenIndustryIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteIndustryCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Industry>()).Returns(IndustryRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonBusinessProfile>()).Returns(ProfileRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Industry>> IndustryRepositoryMock { get; } = new();
        public Mock<IGenericRepository<PersonBusinessProfile>> ProfileRepositoryMock { get; } = new();

        public Industry SetupExisting()
        {
            var entity = Industry.Create(CompanyId, "TECH", "Technology", null);
            IndustryRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteIndustryCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
