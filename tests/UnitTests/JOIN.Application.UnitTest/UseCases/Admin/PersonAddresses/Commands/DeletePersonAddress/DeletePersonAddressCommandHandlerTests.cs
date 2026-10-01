using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonAddresses.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.PersonAddressTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonAddresses.Commands.DeletePersonAddress;

/// <summary>
/// Contains the unit tests for the person address soft-delete command handler.
/// </summary>
public sealed class DeletePersonAddressCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenAddressIsMissing_ShouldReturnNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_ADDRESS_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenNonDefaultAddress_ShouldSoftDeleteWithoutPromotion()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeTrue();
        entity.IsActive.Should().BeFalse();
        entity.GcRecord.Should().NotBe(0);
        context.AddressRepositoryMock.Verify(
            x => x.GetMostRecentActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDefaultAddress_ShouldPromoteMostRecentActiveAddress()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: true);
        var next = new PersonAddress { CompanyId = Ctx.CompanyId, PersonId = Ctx.PersonId };
        context.AddressRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        next.IsDefault.Should().BeTrue();
        context.AddressRepositoryMock.Verify(x => x.UpdateAsync(next), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenDefaultAddressHasNoSuccessor_ShouldStillDelete()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPromotionFails_ShouldReturnInvalidAddressDefault()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExistingAddress(isDefault: true);
        context.AddressRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cannot promote"));

        var response = await CreateHandler(context).Handle(new DeletePersonAddressCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_ADDRESS_DEFAULT");
    }

    private static DeletePersonAddressCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
