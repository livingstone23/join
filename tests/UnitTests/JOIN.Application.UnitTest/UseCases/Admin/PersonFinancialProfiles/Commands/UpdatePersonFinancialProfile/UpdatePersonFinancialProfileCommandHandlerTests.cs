using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.PersonFinancialProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Commands.UpdatePersonFinancialProfile;

/// <summary>
/// Contains the unit tests for the person financial profile update command handler.
/// </summary>
public sealed class UpdatePersonFinancialProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(context, Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenProfileIsMissing_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var act = () => CreateHandler(context).Handle(Command(context, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenProfileBelongsToAnotherPerson_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var act = () => CreateHandler(context).Handle(Command(context, entity.Id) with { PersonId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenIncomeRangeIsMissing_ShouldReturnInvalidReferences()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_REFERENCES");
    }

    [Fact]
    public async Task Handle_WhenSourceOfFundsIsBlank_ShouldReturnInvalidData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id) with { SourceOfFunds = "" }, CancellationToken.None);

        response.Message.Should().Be("INVALID_FINANCIAL_PROFILE_DATA");
    }

    [Fact]
    public async Task Handle_WhenInactiveProfileIsMarkedCurrent_ShouldReturnInvalidCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id) with { IsActive = false, IsCurrent = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_FINANCIAL_PROFILE_CURRENT");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenReactivatedAndMarkedCurrent_ShouldArchiveOthersExcludingItself()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: false);
        entity.Deactivate();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id) with { IsActive = true, IsCurrent = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person financial profile updated successfully.");
        entity.IsActive.Should().BeTrue();
        entity.IsCurrent.Should().BeTrue();
        entity.SourceOfFunds.Should().Be("Business");
        context.ProfileRepositoryMock.Verify(
            x => x.GetActiveCurrentAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenArchived_ShouldRemoveCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id) with { IsCurrent = false }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenFlagsOmitted_ShouldKeepStateUnchanged()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupIncomeRange();
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(context, entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsCurrent.Should().BeTrue();
        entity.IsActive.Should().BeTrue();
    }

    private static UpdatePersonFinancialProfileCommand Command(Ctx context, Guid id) => new()
    {
        Id = id,
        PersonId = Ctx.PersonId,
        IncomeRangeId = context.IncomeRange.Id,
        SourceOfFunds = " Business ",
        DeclaredDate = Ctx.DeclaredDate
    };

    private static UpdatePersonFinancialProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
