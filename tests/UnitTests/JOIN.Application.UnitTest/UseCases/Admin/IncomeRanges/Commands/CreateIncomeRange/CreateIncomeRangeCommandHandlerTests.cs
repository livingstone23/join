using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.IncomeRanges.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.IncomeRanges.Commands.CreateIncomeRange;

/// <summary>
/// Contains the unit tests for the income range creation command handler.
/// </summary>
public sealed class CreateIncomeRangeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext(CompanyId);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    [Fact]
    public async Task Handle_WhenDisplayNameIsInUse_ShouldReturnDisplayNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.RepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([IncomeRange.Create(CompanyId, "low", 0, 100, "USD", 9)]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_DISPLAY_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenDisplayOrderIsInUse_ShouldReturnDisplayOrderInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.RepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([IncomeRange.Create(CompanyId, "Other", 0, 100, "USD", 1)]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INCOME_RANGE_DISPLAY_ORDER_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenDomainRejectsValues_ShouldReturnInvalidValues()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(Command() with { MinimumValue = 500, MaximumValue = 100 }, CancellationToken.None);

        response.Message.Should().Be("INVALID_INCOME_RANGE_VALUES");
        context.RepositoryMock.Verify(x => x.InsertAsync(It.IsAny<IncomeRange>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPayloadIsValid_ShouldCreateIncomeRange()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.RepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Income range created successfully.");
        response.Data!.DisplayName.Should().Be("Low");
        response.Data.CurrencyCode.Should().Be("USD");
        response.Data.MinimumValue.Should().Be(0);
        response.Data.MaximumValue.Should().Be(1000);
        response.Data.DisplayOrder.Should().Be(1);
        response.Data.CompanyName.Should().Be("JOIN CRM");
    }

    private static CreateIncomeRangeCommand Command()
        => new() { DisplayName = " Low ", MinimumValue = 0, MaximumValue = 1000, CurrencyCode = " usd ", DisplayOrder = 1 };

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

        public CreateIncomeRangeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
