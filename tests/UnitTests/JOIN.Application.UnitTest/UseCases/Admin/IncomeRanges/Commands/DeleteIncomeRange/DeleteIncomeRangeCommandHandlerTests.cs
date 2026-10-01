using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Domain.Admin;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Commands.DeleteIncomeRange;

/// <summary>
/// Contains the unit tests for the income range soft-delete command handler.
/// </summary>
public sealed class DeleteIncomeRangeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(new DeleteIncomeRangeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenIncomeRangeIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(new DeleteIncomeRangeCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenRangeIsUsedByFinancialProfile_ShouldReturnInUse()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([PersonFinancialProfile.Create(CompanyId, Guid.NewGuid(), entity.Id, "Salary", DateTime.UtcNow)]);

        var response = await context.CreateHandler().Handle(new DeleteIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(new DeleteIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenRangeIsUnused_ShouldSoftDelete()
    {
        var context = new TestContext(CompanyId);
        var entity = context.SetupExisting();
        context.ProfileRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(new DeleteIncomeRangeCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.GcRecord.Should().NotBe(0);
    }

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<IncomeRange>()).Returns(RepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<PersonFinancialProfile>()).Returns(ProfileRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<IncomeRange>> RepositoryMock { get; } = new();
        public Mock<IGenericRepository<PersonFinancialProfile>> ProfileRepositoryMock { get; } = new();

        public IncomeRange SetupExisting()
        {
            var entity = IncomeRange.Create(CompanyId, "Low", 0, 100, "USD", 1);
            RepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public DeleteIncomeRangeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
