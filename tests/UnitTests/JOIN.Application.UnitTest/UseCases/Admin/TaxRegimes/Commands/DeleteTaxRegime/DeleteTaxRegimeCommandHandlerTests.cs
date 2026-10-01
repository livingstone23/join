using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.TaxRegimes.Commands.DeleteTaxRegime;

/// <summary>
/// Contains the unit tests for the tax regime soft-delete command handler.
/// </summary>
public sealed class DeleteTaxRegimeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenTaxRegimeIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenTaxRegimeIsUsedByBusinessProfile_ShouldReturnInUse()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), entity.Id)]);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_IN_USE");
        context.TaxRegimeRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TaxRegime>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenTaxRegimeIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([PersonBusinessProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), entity.Id)]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<TaxRegime>()).Returns(TaxRegimeRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonBusinessProfile>()).Returns(ProfileRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TaxRegime>> TaxRegimeRepositoryMock { get; } = new();
        public Mock<IGenericRepository<PersonBusinessProfile>> ProfileRepositoryMock { get; } = new();

        public TaxRegime SetupExisting()
        {
            var entity = TaxRegime.Create(CompanyId, "M", "Masculino");
            TaxRegimeRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteTaxRegimeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
