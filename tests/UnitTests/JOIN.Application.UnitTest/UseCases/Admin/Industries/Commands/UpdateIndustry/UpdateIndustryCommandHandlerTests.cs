using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.Industries.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Admin.Industries.Commands.UpdateIndustry;

/// <summary>
/// Contains the unit tests for the industry update command handler.
/// </summary>
public sealed class UpdateIndustryCommandHandlerTests
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
    public async Task Handle_WhenIndustryIsMissing_ShouldReturnNotFound()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();

        var response = await context.CreateHandler().Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenAnotherIndustryUsesCode_ShouldReturnCodeInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, Industry.Create(CompanyId, "fin", "Other", null)]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_CODE_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenAnotherIndustryUsesName_ShouldReturnNameInUse()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync())
            .ReturnsAsync([entity, Industry.Create(CompanyId, "X", "FINANCE", null)]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INDUSTRY_NAME_IN_USE");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);

        var response = await context.CreateHandler().Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Handle_WhenPayloadIsValid_ShouldUpdateIndustry(bool? isActive, bool expectedActive)
    {
        var context = new TestContext(CompanyId);
        context.SetupCompany();
        var entity = context.SetupExisting();
        if (isActive == true)
        {
            entity.Deactivate();
        }

        context.IndustryRepositoryMock.Setup(x => x.GetAllAsync()).ReturnsAsync([entity]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await context.CreateHandler().Handle(Command(entity.Id, isActive, " Banking "), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Industry updated successfully.");
        response.Data!.Code.Should().Be("FIN");
        response.Data.Description.Should().Be("Banking");
        response.Data.IsActive.Should().Be(expectedActive);
    }

    private static UpdateIndustryCommand Command(Guid id, bool? isActive = null, string? description = null)
        => new() { Id = id, Code = " FIN ", Name = " Finance ", Description = description, IsActive = isActive };

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

        public Industry SetupExisting()
        {
            var entity = Industry.Create(CompanyId, "TECH", "Technology", null);
            IndustryRepositoryMock.Setup(x => x.GetAsync(entity.Id)).ReturnsAsync(entity);
            return entity;
        }

        public UpdateIndustryCommandHandler CreateHandler()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
