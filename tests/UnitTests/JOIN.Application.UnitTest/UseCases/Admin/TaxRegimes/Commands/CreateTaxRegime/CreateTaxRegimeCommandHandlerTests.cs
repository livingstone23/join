using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.TaxRegimes.Commands.CreateTaxRegime;

/// <summary>
/// Contains the unit tests for the tax regime creation command handler.
/// </summary>
public sealed class CreateTaxRegimeCommandHandlerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new TestContext(Guid.Empty);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.UnitOfWorkMock.Verify(x => x.GetRepository<Company>(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCompanyDoesNotExist_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext(CompanyId);
        context.CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId)).ReturnsAsync((Company?)null);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    [Fact]
    public async Task Handle_WhenCodeIsInUse_ShouldReturnCodeInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([TaxRegime.Create(CompanyId, "m", "Other name")]);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TAX_REGIME_CODE_IN_USE");
        context.TaxRegimeRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<TaxRegime>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNameIsInUse_ShouldReturnNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([TaxRegime.Create(CompanyId, "X", "masculino")]);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TAX_REGIME_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPayloadIsValid_ShouldCreateTaxRegime()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        // Same code in another tenant and a soft-deleted duplicate must not block creation.
        var deleted = TaxRegime.Create(CompanyId, "M", "Masculino");
        deleted.MarkAsDeleted();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([TaxRegime.Create(Guid.NewGuid(), "M", "Masculino"), deleted]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Tax regime created successfully.");
        response.Data!.CompanyId.Should().Be(CompanyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Code.Should().Be("M");
        response.Data.Name.Should().Be("Masculino");
        response.Data.IsActive.Should().BeTrue();
        context.TaxRegimeRepositoryMock.Verify(x => x.InsertAsync(It.Is<TaxRegime>(g => g.Code == "M" && g.Name == "Masculino")), Times.Once);
    }

    private static CreateTaxRegimeCommand ValidCommand() => new() { Code = " M ", Name = " Masculino " };

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<TaxRegime>()).Returns(TaxRegimeRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<TaxRegime>> TaxRegimeRepositoryMock { get; } = new();

        public void SetupCompany()
            => CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        public CreateTaxRegimeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
