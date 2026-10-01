using FluentAssertions;
using JOIN.Application.Exceptions;
using JOIN.Application.UseCases.Admin.PersonEmployments.Commands;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.PersonEmploymentTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.Commands.UpdatePersonEmployment;

/// <summary>
/// Contains the unit tests for the person employment update command handler.
/// </summary>
public sealed class UpdatePersonEmploymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenEmploymentIsMissing_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var act = () => CreateHandler(context).Handle(Command(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenEmploymentBelongsToAnotherPerson_ShouldThrowNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var act = () => CreateHandler(context).Handle(Command(entity.Id) with { PersonId = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_WhenEndDateIsBeforeStart_ShouldReturnInvalidEmploymentData()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { EndDate = Ctx.StartDate.AddDays(-5) }, CancellationToken.None);

        response.Message.Should().Be("INVALID_EMPLOYMENT_DATA");
    }

    [Fact]
    public async Task Handle_WhenInactiveEmploymentIsMarkedCurrent_ShouldReturnInvalidEmploymentCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsActive = false, IsCurrent = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_EMPLOYMENT_CURRENT");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnUpdateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.Message.Should().Be("UPDATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenMarkedCurrent_ShouldReactivateAndClearOthersExcludingItself()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);
        entity.Deactivate();
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsActive = true, IsCurrent = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person employment updated successfully.");
        entity.IsActive.Should().BeTrue();
        entity.IsCurrent.Should().BeTrue();
        entity.EmployerName.Should().Be("JOIN");
        context.EmploymentRepositoryMock.Verify(
            x => x.GetActiveCurrentAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenUnmarkedCurrent_ShouldRemoveCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id) with { IsCurrent = false }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsCurrent.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_WhenCurrentFlagOmitted_ShouldKeepCurrentState()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.IsCurrent.Should().BeTrue();
    }

    private static UpdatePersonEmploymentCommand Command(Guid id) => new()
    {
        Id = id,
        PersonId = Ctx.PersonId,
        EmployerName = " JOIN ",
        JobTitle = " Lead ",
        StartDate = Ctx.StartDate
    };

    private static UpdatePersonEmploymentCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
