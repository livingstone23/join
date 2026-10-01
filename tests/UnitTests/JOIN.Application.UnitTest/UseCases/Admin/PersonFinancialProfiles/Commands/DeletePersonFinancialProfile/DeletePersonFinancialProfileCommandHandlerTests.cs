using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.PersonFinancialProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Commands.DeletePersonFinancialProfile;

/// <summary>
/// Contains the unit tests for the person financial profile soft-delete command handler.
/// </summary>
public sealed class DeletePersonFinancialProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenProfileIsMissing_ShouldReturnNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_FINANCIAL_PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenNotCurrent_ShouldSoftDeleteWithoutPromotion()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.GcRecord.Should().NotBe(0);
    }

    [Fact]
    public async Task Handle_WhenCurrent_ShouldPromoteMostRecentActiveProfile()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        var next = PersonFinancialProfile.Create(Ctx.CompanyId, Ctx.PersonId, context.IncomeRange.Id, "Other", Ctx.DeclaredDate);
        context.ProfileRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        next.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenCurrentHasNoSuccessor_ShouldStillDelete()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPromotionFails_ShouldReturnInvalidCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        var inactive = PersonFinancialProfile.Create(Ctx.CompanyId, Ctx.PersonId, context.IncomeRange.Id, "Other", Ctx.DeclaredDate);
        inactive.Deactivate();
        context.ProfileRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(inactive);

        var response = await CreateHandler(context).Handle(new DeletePersonFinancialProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_FINANCIAL_PROFILE_CURRENT");
    }

    private static DeletePersonFinancialProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
