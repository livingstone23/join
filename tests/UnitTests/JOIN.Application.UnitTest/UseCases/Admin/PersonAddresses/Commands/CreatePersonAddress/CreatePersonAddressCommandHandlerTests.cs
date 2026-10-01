using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.PersonAddressTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Commands.CreatePersonAddress;

/// <summary>
/// Contains the unit tests for the person address creation command handler.
/// </summary>
public sealed class CreatePersonAddressCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenPersonBelongsToAnotherTenant_ShouldReturnCustomerNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.PersonRepositoryMock.Setup(x => x.GetAsync(Ctx.PersonId)).ReturnsAsync(new Person { CompanyId = Guid.NewGuid() });

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenReferencesDoNotExist_ShouldReturnEveryInvalidReference()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command() with { RegionId = Guid.NewGuid() }, CancellationToken.None);

        response.Message.Should().Be("INVALID_REFERENCES");
        response.Errors.Should().HaveCount(5);
        context.AddressRepositoryMock.Verify(x => x.InsertAsync(It.IsAny<PersonAddress>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupAllReferences();

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("ADDRESS_CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenNotDefault_ShouldCreateNonDefaultAddress()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupAllReferences();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person address created successfully.");
        context.AddressRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonAddress>(a =>
            !a.IsDefault && a.AddressLine1 == "Main St" && a.AddressLine2 == "Apt 2" && a.ZipCode == "1000" && a.CompanyId == Ctx.CompanyId)), Times.Once);
        context.AddressRepositoryMock.Verify(
            x => x.GetActiveWithDefaultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDefault_ShouldClearPreviousDefaultAndCreateDefaultAddress()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupAllReferences();
        var previous = new PersonAddress { CompanyId = Ctx.CompanyId, PersonId = Ctx.PersonId };
        previous.SetAsDefault();
        context.AddressRepositoryMock
            .Setup(x => x.GetActiveWithDefaultAsync(Ctx.CompanyId, Ctx.PersonId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command() with { IsDefault = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        previous.IsDefault.Should().BeFalse();
        context.AddressRepositoryMock.Verify(x => x.UpdateAsync(previous), Times.Once);
        context.AddressRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonAddress>(a => a.IsDefault)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCoordinatorRejectsDefault_ShouldReturnInvalidAddressDefault()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupAllReferences();
        context.AddressRepositoryMock
            .Setup(x => x.GetActiveWithDefaultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await CreateHandler(context).Handle(Command() with { IsDefault = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_ADDRESS_DEFAULT");
        response.Errors.Should().Contain("boom");
    }

    private static CreatePersonAddressCommand Command() => new()
    {
        PersonId = Ctx.PersonId,
        AddressLine1 = " Main St ",
        AddressLine2 = " Apt 2 ",
        ZipCode = " 1000 ",
        StreetTypeId = Guid.NewGuid(),
        CountryId = Guid.NewGuid(),
        ProvinceId = Guid.NewGuid(),
        MunicipalityId = Guid.NewGuid()
    };

    private static CreatePersonAddressCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
