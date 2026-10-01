using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Genders.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Genders.Commands.CreateGender;

/// <summary>
/// Contains the unit tests for the gender creation command handler.
/// </summary>
public sealed class CreateGenderCommandHandlerTests
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
        context.GenderRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Gender.Create(CompanyId, "m", "Other name")]);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("GENDER_CODE_IN_USE");
        context.GenderRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Gender>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNameIsInUse_ShouldReturnNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.GenderRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Gender.Create(CompanyId, "X", "masculino")]);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("GENDER_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.GenderRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPayloadIsValid_ShouldCreateGender()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        // Same code in another tenant and a soft-deleted duplicate must not block creation.
        var deleted = Gender.Create(CompanyId, "M", "Masculino");
        deleted.MarkAsDeleted();
        context.GenderRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Gender.Create(Guid.NewGuid(), "M", "Masculino"), deleted]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(ValidCommand(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Gender created successfully.");
        response.Data!.CompanyId.Should().Be(CompanyId);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.Code.Should().Be("M");
        response.Data.Name.Should().Be("Masculino");
        response.Data.IsActive.Should().BeTrue();
        context.GenderRepositoryMock.Verify(x => x.InsertAsync(It.Is<Gender>(g => g.Code == "M" && g.Name == "Masculino")), Times.Once);
    }

    private static CreateGenderCommand ValidCommand() => new() { Code = " M ", Name = " Masculino " };

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Gender>()).Returns(GenderRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Gender>> GenderRepositoryMock { get; } = new();

        public void SetupCompany()
            => CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        public CreateGenderCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
