using FluentAssertions;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// SPEC 63: win (from any open stage, accepted lines, closing-day rate, customer with origin Lead), lose and reopen.
/// </summary>
public sealed class LeadCloseTests
{
    private static async Task<LeadDto> LeadAsync(LeadTestContext ctx, Guid? personId = null, string relationship = "PROSPECT", Guid? currencyId = null) =>
        (await ctx.SendAsync(new CreateLeadCommand
        {
            PersonId = personId ?? ctx.PersonId, Topic = "T", RelationshipTypeId = ctx.Relationship[relationship], LeadSourceId = ctx.Source["WEB"], CurrencyId = currencyId, EstimatedValue = 50m
        })).Data!.Lead;

    [Fact]
    public async Task Win_FromAnyOpenStage_CreatesTheCustomerWithOriginLead_AndASecondWinConfirmsIt()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx);
        await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["CONTACTED"].Id));

        var won = await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"], Notes = "firmó" });

        won.IsSuccess.Should().BeTrue($"{won.Message} {string.Join(",", won.Errors ?? [])}");
        won.Data!.Lead.Status.Should().Be(3);
        won.Data.Lead.LeadStageId.Should().Be(ctx.Stage["WON"].Id);
        won.Data.Lead.ClosedAt.Should().NotBeNull();
        won.Data.Lead.WonValue.Should().Be(50m, "no lines: the estimated value");
        won.Data.CustomerAction.Should().Be("Created");
        won.Data.Lead.CustomerId.Should().Be(won.Data.CustomerId);
        var history = await ctx.RowsAsync<CustomerStatusHistory>(h => h.CustomerId == won.Data.CustomerId);
        history.Should().ContainSingle(h => h.Action == CustomerStatusAction.Created && h.Source == CustomerStatusSource.Lead && h.SourceReferenceId == lead.Id);
        (await ctx.RowsAsync<LeadStatusHistory>(h => h.LeadId == lead.Id)).Should().Contain(h => h.ToStatus == LeadStatus.Won && h.LeadReasonId == ctx.Reason["BEST_PRICE"]);

        var second = await LeadAsync(ctx);
        var again = await ctx.SendAsync(new WinLeadCommand { Id = second.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] });
        again.Data!.CustomerAction.Should().Be("Confirmed");
        again.Data.CustomerId.Should().Be(won.Data.CustomerId);
        (await ctx.RowsAsync<Customer>(c => c.PersonId == ctx.PersonId)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Win_WithASupplierRelationship_ClosesWithoutCustomer()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx, ctx.OtherPersonId, "SUPPLIER");

        var won = await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] });

        won.Data!.CustomerId.Should().BeNull();
        won.Data.CustomerAction.Should().BeNull();
        (await ctx.RowsAsync<Customer>(c => c.PersonId == ctx.OtherPersonId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Win_NeedsIdentificationWhenTheCompanyRequiresIt_AndSavesIt()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx, ctx.OtherPersonId);
        var command = new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] };

        (await ctx.SendAsync(command)).Message.Should().Be(LeadErrorCodes.PersonIdentificationRequired);
        (await ctx.SendAsync(command with { IdentificationTypeId = Guid.NewGuid(), IdentificationNumber = "1" })).Message.Should().Be(LeadErrorCodes.InvalidReference);
        (await ctx.SendAsync(command with { IdentificationTypeId = ctx.IdentificationTypeId, IdentificationNumber = "001" })).Message.Should().Be(LeadErrorCodes.DuplicateKey, "the other person has 001");

        var won = await ctx.SendAsync(command with { IdentificationTypeId = ctx.IdentificationTypeId, IdentificationNumber = " 1-31-00000-1 " });

        won.Data!.CustomerAction.Should().Be("Created");
        (await ctx.RowsAsync<Person>(p => p.Id == ctx.OtherPersonId)).Single().IdentificationNumber.Should().Be("1-31-00000-1");
    }

    [Fact]
    public async Task Win_WithoutIdentification_WhenTheRuleIsOff()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        await ctx.SendAsync(new SaveLeadCompanySettingsCommand { RequireIdentificationForCustomer = false });
        var lead = await LeadAsync(ctx, ctx.OtherPersonId);

        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] })).Data!.CustomerAction.Should().Be("Created");
    }

    [Fact]
    public async Task Win_AcceptsTheChosenLines_RejectsTheRest_AndFixesTheClosingRate()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx, currencyId: ctx.Usd);
        var a = (await ctx.SendAsync(new AddLeadProductCommand { LeadId = lead.Id, Line = new LeadProductInput { ProductId = ctx.Product["CRM"].Id, Quantity = 2 } })).Data!;
        var b = (await ctx.SendAsync(new AddLeadProductCommand { LeadId = lead.Id, Line = new LeadProductInput { ProductId = ctx.Product["POS"].Id, Quantity = 1 } })).Data!;
        var estimated = (await ctx.LeadAsync(lead.Id)).EstimatedValueBase;
        ctx.Rates.Set(ctx.Usd, 61m);

        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"], AcceptedLeadProductIds = [Guid.NewGuid()] }))
            .Message.Should().Be(LeadErrorCodes.InvalidLeadProduct);
        var won = await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"], AcceptedLeadProductIds = [a.Id] });

        won.Data!.Lead.WonValue.Should().Be(a.LineTotal);
        won.Data.Lead.WonExchangeRate.Should().Be(61m);
        won.Data.Lead.WonValueBase.Should().Be(Math.Round(a.LineTotal * 61m, 2, MidpointRounding.AwayFromZero));
        won.Data.Lead.EstimatedValueBase.Should().Be(estimated, "the funnel value is not touched");
        var lines = await ctx.RowsAsync<LeadProduct>(l => l.LeadId == lead.Id);
        lines.Single(l => l.Id == a.Id).Status.Should().Be(LeadProductStatus.Accepted);
        lines.Single(l => l.Id == b.Id).Status.Should().Be(LeadProductStatus.Rejected);
    }

    [Fact]
    public async Task Win_FailsForWrongReason_NotOpen_RatesAndInactivePerson()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx);

        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["HIGH_PRICE"] })).Message.Should().Be(LeadErrorCodes.InvalidLeadReason);
        ctx.Rates.Remove(ctx.Dop);
        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] })).Message.Should().Be(LeadErrorCodes.ExchangeRateNotFound);
        ctx.Rates.Set(ctx.Dop, 1m, isBase: true);

        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == ctx.PersonId);
        person.Deactivate();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] })).Message.Should().Be(LeadErrorCodes.InvalidPerson);

        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        (await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] })).Message.Should().Be(LeadErrorCodes.LeadNotOpen);
        (await ctx.SendAsync(new LoseLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["HIGH_PRICE"] })).Message.Should().Be(LeadErrorCodes.LeadNotOpen);
        (await ctx.SendAsync(new WinLeadCommand { Id = Guid.NewGuid(), LeadReasonId = ctx.Reason["BEST_PRICE"] })).Message.Should().Be(LeadErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task ReactivatedByAnotherUser_ThenWon_TheCustomerBelongsToTheWinner()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx);
        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        ctx.ActAs(ctx.OtherUserId);
        await ctx.SendAsync(new ReactivateLeadCommand { Id = lead.Id });

        var won = await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] });

        (await ctx.RowsAsync<Customer>(c => c.Id == won.Data!.CustomerId)).Single().UserId.Should().Be(ctx.OtherUserId);
        won.Data!.Lead.OwnerUserId.Should().Be(ctx.OtherUserId);
    }

    [Fact]
    public async Task Lose_NeedsALossReason_AndReopenGoesBackToTheLastOpenStage()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx);
        await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["CONTACTED"].Id));
        await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["QUALIFIED"].Id));

        (await ctx.SendAsync(new LoseLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] })).Message.Should().Be(LeadErrorCodes.InvalidLeadReason);
        (await ctx.SendAsync(new ReopenLeadCommand { Id = lead.Id })).Message.Should().Be(LeadErrorCodes.LeadNotLost);
        var lost = await ctx.SendAsync(new LoseLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["HIGH_PRICE"], CompetitorName = " Competidor " });
        lost.Data!.Status.Should().Be(4);
        lost.Data.LeadStageId.Should().Be(ctx.Stage["LOST"].Id);
        lost.Data.CompetitorName.Should().Be("Competidor");

        var reopened = await ctx.SendAsync(new ReopenLeadCommand { Id = lead.Id, Notes = "volvió" });

        reopened.Data!.Status.Should().Be(1);
        reopened.Data.LeadStageId.Should().Be(ctx.Stage["QUALIFIED"].Id);
        reopened.Data.ClosedAt.Should().BeNull();
        reopened.Data.CompetitorName.Should().BeNull();
        (await ctx.RowsAsync<LeadStatusHistory>(h => h.LeadId == lead.Id)).Should().Contain(h => h.FromStatus == LeadStatus.Lost && h.ToStatus == LeadStatus.Open);
    }

    [Fact]
    public async Task Reopen_UsesTheInitialStageWhenTheLastOneIsInactive_AndAWonLeadIsNotReopened()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await LeadAsync(ctx);
        await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["CONTACTED"].Id));
        await ctx.SendAsync(new LoseLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["HIGH_PRICE"] });
        var contacted = await ctx.Db.LeadStages.IgnoreQueryFilters().SingleAsync(s => s.Id == ctx.Stage["CONTACTED"].Id);
        contacted.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new ReopenLeadCommand { Id = lead.Id })).Data!.LeadStageId.Should().Be(ctx.Stage["NEW"].Id);

        await ctx.SendAsync(new WinLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["BEST_PRICE"] });
        (await ctx.SendAsync(new ReopenLeadCommand { Id = lead.Id })).Message.Should().Be(LeadErrorCodes.LeadNotLost);
    }

    [Fact]
    public void Validators_RejectInvalidInput()
    {
        new WinLeadCommandValidator().Validate(new WinLeadCommand { IdentificationNumber = "1" }).Errors.Should().HaveCount(3);
        new LoseLeadCommandValidator().Validate(new LoseLeadCommand { CompetitorName = new string('c', 201) }).Errors.Should().HaveCount(3);
        new ReopenLeadCommandValidator().Validate(new ReopenLeadCommand()).Errors.Should().HaveCount(1);
    }
}
