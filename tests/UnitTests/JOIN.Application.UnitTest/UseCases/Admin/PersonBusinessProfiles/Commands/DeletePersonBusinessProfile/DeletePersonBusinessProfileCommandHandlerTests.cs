using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.PersonBusinessProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Commands.DeletePersonBusinessProfile;

/// <summary>
/// Contains the unit tests for the person business profile soft-delete command handler.
/// </summary>
public sealed class DeletePersonBusinessProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(new DeletePersonBusinessProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenProfileIsMissing_ShouldReturnNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(new DeletePersonBusinessProfileCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_BUSINESS_PROFILE_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting();

        var response = await CreateHandler(context).Handle(new DeletePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenProfileExists_ShouldSoftDelete()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonBusinessProfileCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.GcRecord.Should().NotBe(0);
    }

    private static DeletePersonBusinessProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object);
}
