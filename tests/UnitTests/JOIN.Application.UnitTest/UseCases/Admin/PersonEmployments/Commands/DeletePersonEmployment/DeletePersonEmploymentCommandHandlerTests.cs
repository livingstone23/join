using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonEmployments.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.PersonEmploymentTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.Commands.DeletePersonEmployment;

/// <summary>
/// Contains the unit tests for the person employment soft-delete command handler.
/// </summary>
public sealed class DeletePersonEmploymentCommandHandlerTests
{
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new Ctx(Guid.Empty);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("COMPANY_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenEmploymentIsMissing_ShouldReturnNotFound()
    {
        var context = new Ctx(Ctx.CompanyId);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(Guid.NewGuid()), CancellationToken.None);

        response.Message.Should().Be("PERSON_EMPLOYMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnDeleteFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("DELETE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenNotCurrent_ShouldSoftDeleteWithoutPromotion()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: false);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        entity.GcRecord.Should().NotBe(0);
        context.EmploymentRepositoryMock.Verify(
            x => x.GetMostRecentActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCurrent_ShouldPromoteMostRecentActiveEmployment()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        var next = PersonEmployment.Create(Ctx.CompanyId, Ctx.PersonId, "Next Co", "Dev", Ctx.StartDate);
        context.EmploymentRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(Ctx.CompanyId, Ctx.PersonId, entity.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(next);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        next.IsCurrent.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenCurrentHasNoSuccessor_ShouldStillDelete()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(entity.Id), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenPromotionFails_ShouldReturnInvalidEmploymentCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        var entity = context.SetupExisting(isCurrent: true);
        var ended = PersonEmployment.Create(Ctx.CompanyId, Ctx.PersonId, "Ended Co", "Dev", Ctx.StartDate);
        ended.MarkAsEnded(Ctx.StartDate.AddYears(1));
        context.EmploymentRepositoryMock
            .Setup(x => x.GetMostRecentActiveAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ended);

        var response = await CreateHandler(context).Handle(new DeletePersonEmploymentCommand(entity.Id), CancellationToken.None);

        response.Message.Should().Be("INVALID_EMPLOYMENT_CURRENT");
    }

    private static DeletePersonEmploymentCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
