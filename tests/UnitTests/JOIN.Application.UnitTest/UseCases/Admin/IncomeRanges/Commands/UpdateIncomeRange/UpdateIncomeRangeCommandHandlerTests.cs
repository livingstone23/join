using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Commands.UpdateIncomeRange;

/// <summary>
/// Contains the unit tests for the income range update command handler.
/// </summary>
public sealed class UpdateIncomeRangeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    [Fact]
    public async Task Handle_WhenIncomeRangeIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();

        var response = await context.CreateHandler().Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenAnotherRangeUsesDisplayName_ShouldReturnDisplayNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.RepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, IncomeRange.Create(CompanyId, "MEDIUM", 0, null, "USD", 7)]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_DISPLAY_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenAnotherRangeUsesDisplayOrder_ShouldReturnDisplayOrderInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.RepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, IncomeRange.Create(CompanyId, "Other", 0, null, "USD", 2)]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_DISPLAY_ORDER_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenDomainRejectsValues_ShouldReturnInvalidValues()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);

        var response = await context.CreateHandler().Handle(Command(entity.Id) with { MaximumValue = 1 }, CancellationToken.None);

        response.Message.Should().Be("INVALID_INCOME_RANGE_VALUES");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Handle_WhenPayloadIsValid_ShouldUpdateIncomeRange(bool? isActive, bool expectedActive)
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        if (isActive == true)
        {
            entity.Deactivate();
        }

        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(Command(entity.Id) with { IsActive = isActive }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Income range updated successfully.");
        response.Data!.DisplayName.Should().Be("Medium");
        response.Data.CurrencyCode.Should().Be("EUR");
        response.Data.DisplayOrder.Should().Be(2);
        response.Data.IsActive.Should().Be(expectedActive);
    }

    private static UpdateIncomeRangeCommand Command(Guid id)
        => new() { Id = id, DisplayName = " Medium ", MinimumValue = 100, MaximumValue = 500, CurrencyCode = "eur", DisplayOrder = 2 };

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<IncomeRange>()).Returns(RepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<IncomeRange>> RepositoryMock { get; } = new();

        public void SetupCompany()
            => CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        public IncomeRange SetupExisting()
        {
            var entity = IncomeRange.Create(CompanyId, "Low", 0, 100, "USD", 1);
            RepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public UpdateIncomeRangeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
