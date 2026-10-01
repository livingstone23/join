using FluentAssertions;
using JOIN.Application.UseCases.Admin.PersonEmployments.Commands;
using JOIN.Domain.Admin;
using Moq;
using Ctx = JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.PersonEmploymentTestContext;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonEmployments.Commands.CreatePersonEmployment;

/// <summary>
/// Contains the unit tests for the person employment creation command handler.
/// </summary>
public sealed class CreatePersonEmploymentCommandHandlerTests
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
    public async Task Handle_WhenEndDateIsBeforeStart_ShouldReturnInvalidEmploymentData()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command() with { EndDate = Ctx.StartDate.AddDays(-1) }, CancellationToken.None);

        response.Message.Should().Be("INVALID_EMPLOYMENT_DATA");
    }

    [Fact]
    public async Task Handle_WhenCoordinatorFails_ShouldReturnInvalidEmploymentCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.EmploymentRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var response = await CreateHandler(context).Handle(Command() with { IsCurrent = true }, CancellationToken.None);

        response.Message.Should().Be("INVALID_EMPLOYMENT_CURRENT");
    }

    [Fact]
    public async Task Handle_WhenSaveAffectsNoRows_ShouldReturnCreateFailed()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();

        var response = await CreateHandler(context).Handle(Command(), CancellationToken.None);

        response.Message.Should().Be("EMPLOYMENT_CREATE_FAILED");
    }

    [Fact]
    public async Task Handle_WhenCurrent_ShouldClearPreviousCurrentEmployment()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        var previous = PersonEmployment.Create(Ctx.CompanyId, Ctx.PersonId, "Old Co", "Dev", Ctx.StartDate.AddYears(-3));
        previous.SetAsCurrent();
        context.EmploymentRepositoryMock
            .Setup(x => x.GetActiveCurrentAsync(Ctx.CompanyId, Ctx.PersonId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([previous]);
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);

        var response = await CreateHandler(context).Handle(Command() with { IsCurrent = true }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Person employment created successfully.");
        previous.IsCurrent.Should().BeFalse();
        context.EmploymentRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonEmployment>(e => e.IsCurrent && e.IsActive)), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenEnded_ShouldPersistEndDateAndNotCurrent()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(
            Command() with { EndDate = Ctx.StartDate.AddYears(1), IsCurrent = true },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.EmploymentRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonEmployment>(e => !e.IsCurrent && e.EndDate == Ctx.StartDate.AddYears(1))), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenInactiveAndNotCurrent_ShouldPersistInactiveEmployment()
    {
        var context = new Ctx(Ctx.CompanyId);
        context.SetupPerson();
        context.UnitOfWorkMock.Setup(x => x.SaveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var response = await CreateHandler(context).Handle(Command() with { IsActive = false }, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.EmploymentRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonEmployment>(e => !e.IsActive && !e.IsCurrent)), Times.Once);
    }

    private static CreatePersonEmploymentCommand Command() => new()
    {
        PersonId = Ctx.PersonId,
        EmployerName = " JOIN ",
        JobTitle = " Engineer ",
        StartDate = Ctx.StartDate
    };

    private static CreatePersonEmploymentCommandHandler CreateHandler(Ctx context)
        => new(context.UnitOfWorkMock.Object, context.CurrentUserServiceMock.Object, context.Coordinator);
}
