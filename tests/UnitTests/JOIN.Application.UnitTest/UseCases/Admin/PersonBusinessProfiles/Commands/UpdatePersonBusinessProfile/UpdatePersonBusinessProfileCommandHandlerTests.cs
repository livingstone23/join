using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.PersonBusinessProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonBusinessProfiles.Commands.UpdatePersonBusinessProfile;

/// <summary>
/// Contains the unit tests for the person business profile update command handler.
/// </summary>
public sealed class UpdatePersonBusinessProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenProfileIsMissing_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var act = () => CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenProfileBelongsToAnotherPerson_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting();

        var act = () => CreateHandler(context).Handle(Command(entity.Id) with { PersonId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenCatalogsBelongToAnotherTenant_ShouldReturnInvalidReferences()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting();
        context.IndustryRepositoryMock.Setup(x => x.GetAsync(It.IsAny<Guid>())).ReturnsAsync(Industry.Create(Guid.NewGuid(), "X", "X", null));

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_REFERENCES");
        response.Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_WhenDomainRejectsIds_ShouldReturnInvalidData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupCatalogs();
        var entity = context.SetupExisting();

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { TaxRegimeId = Guid.Empty }, CancellationToken.None);

        response.Message.Should().Be("INVALID_BUSINESS_PROFILE_DATA");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupCatalogs();
        var entity = context.SetupExisting();

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenReactivated_ShouldDeactivateOtherProfilesExcludingItself()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupCatalogs();
        var entity = context.SetupExisting();
        entity.Deactivate();
        var other = PersonBusinessProfile.Create(Ctx.CompanyId, Ctx.PersonId, Guid.NewGuid(), Guid.NewGuid());
        context.ProfileRepositoryMock
            .Setup(x => x.GetActiveProfilesAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([other]);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsActive = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person business profile updated successfully.");
        entity.IsActive.Should().BeTrue();
        other.IsActive.Should().BeFalse();
        entity.Website.Should().Be("https://new.test");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(null, true)]
    public async Task Handle_WhenActiveFlagIsFalseOrOmitted_ShouldApplyIt(bool? isActive, bool expected)
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupCatalogs();
        var entity = context.SetupExisting();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsActive = isActive }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsActive.Should().Be(expected);
    }

    private static UpdatePersonBusinessProfileCommand Command(Guid id) => new()
    {
        Id = id,
        PersonId = Ctx.PersonId,
        IndustryId = Guid.NewGuid(),
        TaxRegimeId = Guid.NewGuid(),
        Website = " https://new.test "
    };

    private static UpdatePersonBusinessProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
