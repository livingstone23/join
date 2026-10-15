using FluentAssertions;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Customers.Customers;

/// <summary>
/// SPEC 55 rules 1–7 over the real handlers and <see cref="ICustomerActivationService"/>: one customer per person and
/// company, idempotent activation, history of every event, stage independent of the state.
/// </summary>
public sealed class CustomerLifecycleTests
{
    [Fact]
    public async Task Create_TwiceForTheSamePerson_ShouldKeepOneRowAndConfirm()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        var first = await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId));
        var second = await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId));

        first.IsSuccess.Should().BeTrue(first.Message);
        first.Message.Should().Be("Created");
        first.Data!.CustomerCode.Should().Be("C000000001");
        first.Data.CustomerStageCode.Should().Be(CustomerStage.Codes.Customer, "without a stage the company's default is used");
        first.Data.UserId.Should().Be(ctx.UserId, "the registering user is the authenticated one");
        first.Data.LastStatusAction.Should().Be((int)CustomerStatusAction.Created);
        second.Message.Should().Be("Confirmed");
        second.Data!.Id.Should().Be(first.Data.Id);
        second.Data.CustomerCode.Should().Be(first.Data.CustomerCode);

        (await ctx.CustomersAsync()).Should().ContainSingle();
        (await ctx.HistoryAsync(first.Data.Id)).Select(h => h.Action)
            .Should().Equal(CustomerStatusAction.Created, CustomerStatusAction.Confirmed);
        ctx.UnitOfWork.CustomerLockRequests.Should().Be(2, "every activation takes the per-person lock");
    }

    [Fact]
    public async Task Create_WithAnExplicitStage_ShouldUseIt()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        var response = await ctx.SendAsync(new CreateCustomerCommand(ctx.OtherPersonId, ctx.Stage[CustomerStage.Codes.Lead]));

        response.Data!.CustomerStageCode.Should().Be(CustomerStage.Codes.Lead);
        response.Data.PersonName.Should().Be("Osorio SRL");
    }

    [Fact]
    public async Task Create_ForAnInactiveCustomer_ShouldReactivateTheSameRow()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;
        (await ctx.SendAsync(new DeactivateCustomerCommand(created.Id, "Sin actividad"))).Message.Should().Be("Deactivated");

        var again = await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId));

        again.Message.Should().Be("Activated");
        again.Data!.Id.Should().Be(created.Id);
        again.Data.IsActive.Should().BeTrue();
        again.Data.DeactivatedAt.Should().BeNull();
        var history = await ctx.HistoryAsync(created.Id);
        history.Select(h => h.Action).Should().Equal(CustomerStatusAction.Created, CustomerStatusAction.Deactivated, CustomerStatusAction.Activated);
        history[1].Notes.Should().Be("Sin actividad");
        history.Should().OnlyContain(h => h.Source == CustomerStatusSource.Manual && h.UserId == ctx.UserId);
    }

    [Fact]
    public async Task Deactivate_WhenAlreadyInactive_ShouldChangeNothingAndWriteNoHistory()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;
        await ctx.SendAsync(new DeactivateCustomerCommand(created.Id));

        var again = await ctx.SendAsync(new DeactivateCustomerCommand(created.Id));

        again.IsSuccess.Should().BeTrue();
        again.Message.Should().Be("Unchanged");
        again.Data!.IsActive.Should().BeFalse();
        (await ctx.HistoryAsync(created.Id)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Activate_WhenActive_ShouldConfirm_AndWhenInactiveShouldReactivate()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;

        (await ctx.SendAsync(new ActivateCustomerCommand(created.Id))).Message.Should().Be("Confirmed");
        await ctx.SendAsync(new DeactivateCustomerCommand(created.Id));
        var reactivated = await ctx.SendAsync(new ActivateCustomerCommand(created.Id, "Volvió"));

        reactivated.Message.Should().Be("Activated");
        reactivated.Data!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Activate_WhenThePersonIsInactive_ShouldAnswerInvalidPerson()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;
        await ctx.SendAsync(new DeactivateCustomerCommand(created.Id));
        await ctx.DeactivatePersonAsync(ctx.PersonId);

        var response = await ctx.SendAsync(new ActivateCustomerCommand(created.Id));

        response.Message.Should().Be(CustomerErrorCodes.InvalidPerson);
        (await ctx.CustomersAsync()).Single().IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Create_ForAnInactiveOrForeignPerson_ShouldAnswerInvalidPerson()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        await ctx.DeactivatePersonAsync(ctx.PersonId);

        (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Message.Should().Be(CustomerErrorCodes.InvalidPerson);
        (await ctx.SendAsync(new CreateCustomerCommand(Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.InvalidPerson);
        (await ctx.CustomersAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithAnInactiveOrUnknownStage_ShouldAnswerInvalidCustomerStage()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var former = ctx.Db.CustomerStages.Single(s => s.Id == ctx.Stage[CustomerStage.Codes.Former]);
        former.Deactivate();
        await ctx.Db.SaveChangesAsync();

        (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId, former.Id))).Message.Should().Be(CustomerErrorCodes.InvalidCustomerStage);
        (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId, Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.InvalidCustomerStage);
    }

    [Fact]
    public async Task Update_ShouldChangeOnlyTheStage()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;
        await ctx.SendAsync(new DeactivateCustomerCommand(created.Id));

        var updated = await ctx.SendAsync(new UpdateCustomerCommand(created.Id, ctx.Stage[CustomerStage.Codes.Prospect]));
        var unchanged = await ctx.SendAsync(new UpdateCustomerCommand(created.Id, ctx.Stage[CustomerStage.Codes.Prospect]));

        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.CustomerStageCode.Should().Be(CustomerStage.Codes.Prospect);
        updated.Data.IsActive.Should().BeFalse("the stage never changes the state (rule 6)");
        updated.Data.LastStatusAction.Should().Be((int)CustomerStatusAction.Deactivated);
        unchanged.IsSuccess.Should().BeTrue("saving the same stage is not a failure");
    }

    [Fact]
    public async Task Update_WithErrors_ShouldAnswerTheirCodes()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;

        (await ctx.SendAsync(new UpdateCustomerCommand(Guid.NewGuid(), ctx.Stage[CustomerStage.Codes.Lead]))).Message
            .Should().Be(CustomerErrorCodes.CustomerNotFound);
        (await ctx.SendAsync(new UpdateCustomerCommand(created.Id, Guid.NewGuid()))).Message
            .Should().Be(CustomerErrorCodes.InvalidCustomerStage);
        (await ctx.SendAsync(new DeactivateCustomerCommand(Guid.NewGuid()))).Message
            .Should().Be(CustomerErrorCodes.CustomerNotFound);
        (await ctx.SendAsync(new ActivateCustomerCommand(Guid.NewGuid()))).Message
            .Should().Be(CustomerErrorCodes.CustomerNotFound);
    }

    [Fact]
    public async Task Commands_WithoutCompanyOrUser_ShouldAnswerContextErrors()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        ctx.ActingUserId = null;

        (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Message.Should().Be(CustomerErrorCodes.UserRequired);
        (await ctx.SendAsync(new ActivateCustomerCommand(Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.UserRequired);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCustomerCommand(Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeactivateCustomerCommand(Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task PersonDeactivation_ThroughTheService_ShouldLogPersonDeactivated()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;
        var service = ctx.Get<ICustomerActivationService>();

        var result = await service.DeactivateAsync(ctx.CompanyId, created.Id, CustomerStatusSource.PersonDeactivated, ctx.UserId, ctx.PersonId);
        await ctx.UnitOfWork.SaveChangesAsync();

        result.Data!.Action.Should().Be(CustomerStatusAction.Deactivated);
        var last = (await ctx.HistoryAsync(created.Id)).Last();
        last.Source.Should().Be(CustomerStatusSource.PersonDeactivated);
        last.SourceReferenceId.Should().Be(ctx.PersonId);
    }

    [Fact]
    public void Validators_ShouldRejectEmptyIdsAndLongNotes()
    {
        new CreateCustomerCommandValidator().Validate(new CreateCustomerCommand(Guid.Empty, Guid.Empty))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["PersonId", "CustomerStageId"]);
        new CreateCustomerCommandValidator().Validate(new CreateCustomerCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
        new UpdateCustomerCommandValidator().Validate(new UpdateCustomerCommand(Guid.Empty, Guid.Empty))
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "CustomerStageId"]);
        new ActivateCustomerCommandValidator().Validate(new ActivateCustomerCommand(Guid.NewGuid(), new string('x', 501)))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Notes");
        new DeactivateCustomerCommandValidator().Validate(new DeactivateCustomerCommand(Guid.Empty))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Id");
    }

    [Fact]
    public void Customer_Domain_ShouldGuardItsArguments()
    {
        var act = () => Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C000000001", Guid.Empty);
        act.Should().Throw<ArgumentException>();
        var tooLong = () => Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C0000000001", Guid.NewGuid());
        tooLong.Should().Throw<ArgumentException>();
        var customer = Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " C000000001 ", Guid.NewGuid());
        customer.CustomerCode.Should().Be("C000000001");
        var change = () => customer.ChangeStage(Guid.Empty);
        change.Should().Throw<ArgumentException>();
        var history = () => CustomerStatusHistory.Create(Guid.NewGuid(), Guid.NewGuid(), CustomerStatusAction.Created, CustomerStatusSource.Manual, Guid.Empty);
        history.Should().Throw<ArgumentException>();
    }
}
