using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Industries.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Industries.Commands.CreateIndustry;

/// <summary>
/// Contains the unit tests for the industry creation command handler.
/// </summary>
public sealed class CreateIndustryCommandHandlerTests
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
    public async Task Handle_WhenCodeIsInUse_ShouldReturnCodeInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Industry.Create(CompanyId, "tech", "Other", null)]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_CODE_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenNameIsInUse_ShouldReturnNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([Industry.Create(CompanyId, "OTHER", "technology", null)]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);

        var response = await context.CreateHandler().Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CREATE_FAILED");
    }

    [Theory]
    [InlineData("  Software  ", "Software")]
    [InlineData("   ", null)]
    public async Task Handle_WhenPayloadIsValid_ShouldCreateIndustryWithNormalizedDescription(string description, string? expected)
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(Command(description), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Industry created successfully.");
        response.Data!.Code.Should().Be("TECH");
        response.Data.Name.Should().Be("Technology");
        response.Data.Description.Should().Be(expected);
        response.Data.CompanyName.Should().Be("JOIN CRM");
        response.Data.IsActive.Should().BeTrue();
        context.IndustryRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<Industry>()), Times.Once);
    }

    private static CreateIndustryCommand Command(string? description = null)
        => new() { Code = " TECH ", Name = " Technology ", Description = description };

    private sealed class TestContext
    {
        public TestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            UnitOfWorkMock.Setup(x => x.GetRepository<Company>()).Returns(CompanyRepositoryMock.Object);
            UnitOfWorkMock.Setup(x => x.GetRepository<Industry>()).Returns(IndustryRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Company>> CompanyRepositoryMock { get; } = new();
        public Mock<IGenericRepository<Industry>> IndustryRepositoryMock { get; } = new();

        public void SetupCompany()
            => CompanyRepositoryMock.Setup(x => x.GetAsync(CompanyId))
                .ReturnsAsync(new Company { Name = "JOIN CRM", TaxId = "RUC" });

        public CreateIndustryCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
