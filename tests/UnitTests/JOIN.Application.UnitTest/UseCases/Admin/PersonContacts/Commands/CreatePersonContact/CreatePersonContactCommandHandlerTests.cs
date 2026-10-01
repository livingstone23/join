using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonContacts.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.PersonContactTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonContacts.Commands.CreatePersonContact;

/// <summary>
/// Contains the unit tests for the person contact creation command handler.
/// </summary>
public sealed class CreatePersonContactCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenPersonIsMissing_ShouldReturnPersonNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("PERSON_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenContactValueIsBlank_ShouldReturnInvalidContactData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command() with { ContactValue = "  " }, CancellationToken.None);

        response.Message.Should().Be("INVALID_CONTACT_DATA");
    }

    [Fact]
    public async Task Handle_WhenCoordinatorFails_ShouldReturnInvalidContactPrimary()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.ContactRepositoryMock
            .Setup(x => x.GetActiveWithPrimaryByTypeAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ContactType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await CreateHandler(context).Handle(Command() with { IsPrimary = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_CONTACT_PRIMARY");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CONTACT_CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenPrimary_ShouldClearOtherPrimariesOfSameType()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        var previous = PersonContact.Create(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, "prev@join.test");
        previous.SetAsPrimary();
        context.ContactRepositoryMock
            .Setup(x => x.GetActiveWithPrimaryByTypeAsync(Ctx.CompanyId, Ctx.PersonId, ContactType.PrimaryEmail, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command() with { IsPrimary = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person contact created successfully.");
        previous.IsPrimary.Should().BeFalse();
        context.ContactRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonContact>(c => c.IsPrimary && c.ContactValue == "ana@join.test")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNotPrimary_ShouldCreateSecondaryContact()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ContactRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonContact>(c => !c.IsPrimary)), Times.Once);
    }

    private static CreatePersonContactCommand Command() => new()
    {
        PersonId = Ctx.PersonId,
        ContactType = ContactType.PrimaryEmail,
        ContactValue = " ana@join.test ",
        Comments = "work"
    };

    private static CreatePersonContactCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
