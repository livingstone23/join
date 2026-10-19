using FluentAssertions;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Customers.Customers;

/// <summary>
/// SPEC 66: every stage change of a customer is a <see cref="CustomerStatusAction.StageChanged"/> row of its status
/// history, with both stages, the user and the optional notes; the same stage writes nothing.
/// </summary>
public sealed class CustomerStageChangeHistoryTests
{
    [Fact]
    public async Task Update_ToAnotherStage_ShouldLogStageChangedWithNotes()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;

        var updated = await ctx.SendAsync(new UpdateCustomerCommand(created.Id, ctx.Stage[CustomerStage.Codes.Recurring], "  Compra cada mes  "));

        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.LastStatusAction.Should().Be((int)CustomerStatusAction.Created, "the last status ignores stage changes (rule 7)");
        var row = (await ctx.HistoryAsync(created.Id)).Should().ContainSingle(h => h.Action == CustomerStatusAction.StageChanged).Subject;
        row.FromCustomerStageId.Should().Be(ctx.Stage[CustomerStage.Codes.Customer]);
        row.ToCustomerStageId.Should().Be(ctx.Stage[CustomerStage.Codes.Recurring]);
        row.Notes.Should().Be("Compra cada mes");
        row.Source.Should().Be(CustomerStatusSource.Manual);
        row.UserId.Should().Be(ctx.UserId);
    }

    [Fact]
    public async Task Update_WithoutNotes_ShouldLogTheRowWithNullNotes()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;

        await ctx.SendAsync(new UpdateCustomerCommand(created.Id, ctx.Stage[CustomerStage.Codes.Recurring]));

        (await ctx.HistoryAsync(created.Id)).Single(h => h.Action == CustomerStatusAction.StageChanged).Notes.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithTheSameStage_ShouldNotLogAnything()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var created = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId))).Data!;

        var unchanged = await ctx.SendAsync(new UpdateCustomerCommand(created.Id, created.CustomerStageId, "Sin cambio"));

        unchanged.IsSuccess.Should().BeTrue(unchanged.Message);
        (await ctx.HistoryAsync(created.Id)).Select(h => h.Action).Should().Equal(CustomerStatusAction.Created);
    }

    [Fact]
    public void Validator_ShouldRejectNotesLongerThan500()
    {
        var validator = new UpdateCustomerCommandValidator();

        validator.Validate(new UpdateCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), new string('x', 501)))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Notes");
        validator.Validate(new UpdateCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), new string('x', 500))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Domain_ChangeStage_ShouldReturnTheRowOnlyOnARealChange()
    {
        var from = Guid.NewGuid();
        var to = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customer = Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C000000001", from);

        customer.ChangeStage(from, userId).Should().BeNull();
        var row = customer.ChangeStage(to, userId, " ", CustomerStatusSource.Migration);

        row.Should().NotBeNull();
        row!.Action.Should().Be(CustomerStatusAction.StageChanged);
        row.Source.Should().Be(CustomerStatusSource.Migration);
        row.FromCustomerStageId.Should().Be(from);
        row.ToCustomerStageId.Should().Be(to);
        row.CustomerId.Should().Be(customer.Id);
        row.Notes.Should().BeNull();
        customer.CustomerStageId.Should().Be(to);
        var noFrom = () => CustomerStatusHistory.CreateStageChange(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, to, userId);
        noFrom.Should().Throw<ArgumentException>();
        var noTo = () => CustomerStatusHistory.CreateStageChange(Guid.NewGuid(), Guid.NewGuid(), from, Guid.Empty, userId);
        noTo.Should().Throw<ArgumentException>();
    }
}
