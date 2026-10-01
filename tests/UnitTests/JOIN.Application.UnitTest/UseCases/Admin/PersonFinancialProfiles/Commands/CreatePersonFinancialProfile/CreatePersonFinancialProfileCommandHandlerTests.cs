using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.PersonFinancialProfileTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonFinancialProfiles.Commands.CreatePersonFinancialProfile;

/// <summary>
/// Contains the unit tests for the person financial profile creation command handler.
/// </summary>
public sealed class CreatePersonFinancialProfileCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(context), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenPersonIsMissing_ShouldReturnPersonNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(Command(context), CancellationToken.None);

        response.Message.Should().Be("PERSON_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenIncomeRangeBelongsToAnotherTenant_ShouldReturnInvalidReferences()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        var foreign = IncomeRange.Create(Guid.NewGuid(), "Other", 0, null, "USD", 1);
        context.IncomeRangeRepositoryMock.Setup(x => x.GetAsync(foreign.Id)).ReturnsAsync(foreign);

        var response = await CreateHandler(context).Handle(Command(context) with { IncomeRangeId = foreign.Id }, CancellationToken.None);

        response.Message.Should().Be("INVALID_REFERENCES");
    }

    [Fact]
    public async Task Handle_WhenSourceOfFundsIsBlank_ShouldReturnInvalidData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupIncomeRange();

        var response = await CreateHandler(context).Handle(Command(context) with { SourceOfFunds = " " }, CancellationToken.None);

        response.Message.Should().Be("INVALID_FINANCIAL_PROFILE_DATA");
    }

    [Fact]
    public async Task Handle_WhenCoordinatorFails_ShouldReturnInvalidCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupIncomeRange();
        context.ProfileRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await CreateHandler(context).Handle(Command(context), CancellationToken.None);

        response.Message.Should().Be("INVALID_FINANCIAL_PROFILE_CURRENT");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupIncomeRange();

        var response = await CreateHandler(context).Handle(Command(context), CancellationToken.None);

        response.Message.Should().Be("FINANCIAL_PROFILE_CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenCurrentByDefault_ShouldArchivePreviousCurrentProfile()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupIncomeRange();
        var previous = PersonFinancialProfile.Create(Ctx.CompanyId, Ctx.PersonId, context.IncomeRange.Id, "Old", Ctx.DeclaredDate.AddYears(-1));
        previous.SetAsCurrent();
        context.ProfileRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(Ctx.CompanyId, Ctx.PersonId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command(context), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person financial profile created successfully.");
        previous.IsCurrent.Should().BeFalse();
        context.ProfileRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonFinancialProfile>(p => p.IsCurrent && p.SourceOfFunds == "Salary")), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenNotCurrentAndInactive_ShouldPersistArchivedInactiveProfile()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.SetupIncomeRange();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(context) with { IsCurrent = false, IsActive = false }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ProfileRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonFinancialProfile>(p => !p.IsCurrent && !p.IsActive)), Times.Once);
        context.ProfileRepositoryMock.Verify(
            x => x.GetActiveCurrentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreatePersonFinancialProfileCommand Command(Ctx context) => new()
    {
        PersonId = Ctx.PersonId,
        IncomeRangeId = context.IncomeRange.Id,
        SourceOfFunds = " Salary ",
        DeclaredDate = Ctx.DeclaredDate
    };

    private static CreatePersonFinancialProfileCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
