using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;
using JOIN.Domain.Admin;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Domain.Audit;
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
        context.ProfileRepositoryMock.SetupRows([PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), entity.Id)]);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_IN_USE");
        context.TaxRegimeRepositoryMock.Verify(x => x.UpdateAsync(It.IsAny<TaxRegime>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.SetupRows([]);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenTaxRegimeIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        var deletedProfile = PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), entity.Id);
        deletedProfile.MarkAsDeleted();
        context.ProfileRepositoryMock.SetupRows([deletedProfile]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

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
        var deletedProfile = PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), entity.Id);
        deletedProfile.MarkAsDeleted();
        context.ProfileRepositoryMock.SetupRows([deletedProfile]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = PersonBusinessProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), entity.Id);
        context.UnitOfWorkMock.SetupRepositoryRows<PersonBusinessProfile>([child1]);


        var response = await context.CreateHandler().Handle(new DeleteTaxRegimeCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TAX_REGIME_IN_USE");
        response.Errors.Should().Equal("Active business profiles: 1");
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
        var deletedProfile = PersonBusinessProfile.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), entity.Id);
        deletedProfile.MarkAsDeleted();
        context.ProfileRepositoryMock.SetupRows([deletedProfile]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Arrange: one row per child type that blocks the delete (SPEC 41).
        var child1 = PersonBusinessProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), entity.Id);
        child1.MarkAsDeleted();
        context.UnitOfWorkMock.SetupRepositoryRows<PersonBusinessProfile>([child1]);


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

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new() { DefaultValue = DefaultValue.Mock };
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
