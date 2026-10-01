using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.PersonBusinessProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Commands.CreatePersonBusinessProfile;

/// <summary>
/// Contains the unit tests for the person business profile creation command handler.
/// </summary>
public sealed class CreatePersonBusinessProfileCommandHandlerTests
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
    public async Task Handle_WhenCatalogsAreMissing_ShouldReturnBothReferenceErrors()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("INVALID_REFERENCES");
        response.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WhenDomainRejectsIds_ShouldReturnInvalidData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupCatalogs();

        var response = await CreateHandler(context).Handle(Command() with { IndustryId = Guid.Empty }, CancellationToken.None);

        response.Message.Should().Be("INVALID_BUSINESS_PROFILE_DATA");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupCatalogs();

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("BUSINESS_PROFILE_CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenActive_ShouldDeactivateOtherActiveProfiles()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupCatalogs();
        var previous = PersonBusinessProfile.Create(Ctx.CompanyId, Ctx.PersonId, Guid.NewGuid(), Guid.NewGuid());
        context.ProfileRepositoryMock
            .Setup(x => x.GetActiveProfilesAsync(Ctx.CompanyId, Ctx.PersonId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person business profile created successfully.");
        previous.IsActive.Should().BeFalse();
        context.ProfileRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonBusinessProfile>(p => p.IsActive && p.Website == "https://acme.test")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenInactive_ShouldNotTouchOtherProfiles()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupCatalogs();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command() with { IsActive = false }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ProfileRepositoryMock.Verify(
            x => x.GetActiveProfilesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        context.ProfileRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonBusinessProfile>(p => !p.IsActive)), Times.Once);
    }

    private static CreatePersonBusinessProfileCommand Command() => new()
    {
        PersonId = Ctx.PersonId,
        IndustryId = Guid.NewGuid(),
        TaxRegimeId = Guid.NewGuid(),
        Website = " https://acme.test ",
        FoundationDate = new DateTime(2010, 3, 1)
    };

    private static CreatePersonBusinessProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
