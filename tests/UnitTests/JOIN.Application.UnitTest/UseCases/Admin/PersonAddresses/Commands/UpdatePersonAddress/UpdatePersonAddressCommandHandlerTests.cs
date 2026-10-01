using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.PersonAddressTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Commands.UpdatePersonAddress;

/// <summary>
/// Contains the unit tests for the person address update command handler.
/// </summary>
public sealed class UpdatePersonAddressCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenAddressIsMissing_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var act = () => CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenAddressBelongsToAnotherPerson_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);

        var act = () => CreateHandler(context).Handle(Command(entity.Id) with { PersonId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenMarkedDefault_ShouldClearOthersExcludingItselfAndPersist()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsDefault = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(entity.Id);
        entity.IsDefault.Should().BeTrue();
        entity.AddressLine1.Should().Be("New St");
        entity.AddressLine2.Should().BeNull();
        entity.ZipCode.Should().Be("2000");
        context.AddressRepositoryMock.Verify(
            x => x.GetActiveWithDefaultAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUnmarkedDefault_ShouldRemoveDefault()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenAddressCannotBeDefault_ShouldReturnInvalidAddressDefault()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);
        entity.Deactivate();

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsDefault = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_ADDRESS_DEFAULT");
        context.AddressRepositoryMock.Verify(x => x.UpdateAsync(entity), Times.Never);
    }

    private static UpdatePersonAddressCommand Command(Guid id) => new()
    {
        Id = id,
        PersonId = Ctx.PersonId,
        AddressLine1 = " New St ",
        AddressLine2 = null,
        ZipCode = " 2000 ",
        StreetTypeId = Guid.NewGuid(),
        CountryId = Guid.NewGuid(),
        RegionId = Guid.NewGuid(),
        ProvinceId = Guid.NewGuid(),
        MunicipalityId = Guid.NewGuid()
    };

    private static UpdatePersonAddressCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
