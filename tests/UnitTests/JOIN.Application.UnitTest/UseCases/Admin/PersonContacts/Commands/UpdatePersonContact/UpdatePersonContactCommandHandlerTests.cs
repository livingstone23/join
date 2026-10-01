using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.UseCases.Admin.PersonContacts.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.PersonContactTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.Commands.UpdatePersonContact;

/// <summary>
/// Contains the unit tests for the person contact update command handler.
/// </summary>
public sealed class UpdatePersonContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenContactIsMissing_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var act = () => CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenContactBelongsToAnotherPerson_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);

        var act = () => CreateHandler(context).Handle(Command(entity.Id) with { PersonId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenContactValueIsBlank_ShouldReturnInvalidContactData()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { ContactValue = " " }, CancellationToken.None);

        response.Message.Should().Be("INVALID_CONTACT_DATA");
    }

    [Fact]
    public async Task Handle_WhenInactiveContactIsMarkedPrimary_ShouldReturnInvalidContactPrimary()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);
        entity.Deactivate();

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsPrimary = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_CONTACT_PRIMARY");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPrimaryContactChangesType_ShouldPromoteSuccessorOfPreviousType()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: true, ContactType.PrimaryEmail);
        var successor = PersonContact.Create(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, "next@join.test");
        context.ContactRepositoryMock
            .Setup(x => x.GetMostRecentActiveByTypeAsync(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(successor);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(
            Command(entity.Id) with { ContactType = ContactType.MobilePhone, ContactValue = "+507 6000-0000", IsPrimary = true },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person contact updated successfully.");
        successor.IsPrimary.Should().BeTrue();
        entity.ContactType.Should().Be(ContactType.MobilePhone);
        entity.IsPrimary.Should().BeTrue();
        context.ContactRepositoryMock.Verify(
            x => x.GetActiveWithPrimaryByTypeAsync(Ctx.CompanyId, Ctx.PersonId, ContactType.MobilePhone, entity.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPrimaryChangesTypeWithoutSuccessor_ShouldNotPromote()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingContact(isPrimary: true, ContactType.PrimaryEmail);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(
            Command(entity.Id) with { ContactType = ContactType.MobilePhone },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsPrimary.Should().BeFalse();
    }

    private static UpdatePersonContactCommand Command(Guid id) => new()
    {
        Id = id,
        PersonId = Ctx.PersonId,
        ContactType = ContactType.PrimaryEmail,
        ContactValue = " new@join.test ",
        Comments = null
    };

    private static UpdatePersonContactCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
