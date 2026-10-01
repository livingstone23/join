using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.TaxRegimes.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.TaxRegimes.Commands.UpdateTaxRegime;

/// <summary>
/// Contains the unit tests for the tax regime update command handler.
/// </summary>
public sealed class UpdateTaxRegimeCommandHandlerTests
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
    public async Task Handle_WhenTaxRegimeBelongsToAnotherTenant_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var foreign = TaxRegime.Create(Guid.NewGuid(), "M", "Masculino");
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAsync(foreign.Id)).ReturnsAsync(foreign);

        var response = await context.CreateHandler().Handle(Command(foreign.Id), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenTaxRegimeIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();

        var response = await context.CreateHandler().Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenAnotherTaxRegimeUsesCode_ShouldReturnCodeInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, TaxRegime.Create(CompanyId, "f", "Otro")]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_CODE_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenAnotherTaxRegimeUsesName_ShouldReturnNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, TaxRegime.Create(CompanyId, "Z", "FEMENINO")]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("TAX_REGIME_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Handle_WhenPayloadIsValid_ShouldUpdateTaxRegimeAndApplyActiveState(bool? isActive, bool expectedActive)
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        if (isActive == true)
        {
            entity.Deactivate();
        }

        context.TaxRegimeRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(Command(entity.Id, isActive), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Tax regime updated successfully.");
        response.Data!.Code.Should().Be("F");
        response.Data.Name.Should().Be("Femenino");
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.IsActive.Should().Be(expectedActive);
        context.TaxRegimeRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Once);
    }

    private static UpdateTaxRegimeCommand Command(Guid id, bool? isActive = null)
        => new() { Id = id, Code = " F ", Name = " Femenino ", IsActive = isActive };

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

        public TaxRegime SetupExisting()
        {
            var entity = TaxRegime.Create(CompanyId, "M", "Masculino");
            TaxRegimeRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public UpdateTaxRegimeCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
