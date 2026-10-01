using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonContacts.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.PersonContactTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.Commands.DeletePersonContact;

/// <summary>
/// Contains the unit tests for the person contact soft-delete command handler.
/// </summary>
public sealed class DeletePersonContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenContactIsMissing_ShouldReturnNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_CONTACT_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenSecondaryContact_ShouldSoftDeleteWithoutPromotion()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsActive.Should().BeFalse();
        entity.GcRecord.Should().NotBe(0);
    }

    [Fact]
    public async Task Handle_WhenPrimaryContact_ShouldPromoteNextOfSameType()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: true);
        var next = PersonContact.Create(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, "next@join.test");
        context.ContactRepositoryMock
            .Setup(x => x.GetMostRecentActiveByTypeAsync(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        next.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPromotionFails_ShouldReturnInvalidContactPrimary()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: true);
        context.ContactRepositoryMock
            .Setup(x => x.GetMostRecentActiveByTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactType>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await CreateHandler(context).Handle(new DeletePersonContactCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_CONTACT_PRIMARY");
    }

    private static DeletePersonContactCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
