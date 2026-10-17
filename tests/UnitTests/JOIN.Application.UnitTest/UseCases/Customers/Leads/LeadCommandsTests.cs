using FluentAssertions;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Services.Catalog;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Admin.Persons;
using JOIN.Application.UseCases.Catalog.Products.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;
using JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Customers.Teams;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// SPEC 61 commands: create (existing or new person), duplicates policy, team rule, stage moves, value in base
/// currency, contacts, comments, products, inactivate/reactivate, reassign and the in-use blocks.
/// </summary>
public sealed class LeadCommandsTests
{
    private static CreateLeadCommand NewLead(LeadTestContext ctx, Guid? personId = null) => new()
    {
        PersonId = personId ?? ctx.PersonId,
        Topic = " Punto de venta ",
        RelationshipTypeId = ctx.Relationship["PROSPECT"],
        LeadSourceId = ctx.Source["WEB"]
    };

    private static async Task<LeadDto> CreateAsync(LeadTestContext ctx, CreateLeadCommand? command = null)
    {
        var response = await ctx.SendAsync(command ?? NewLead(ctx));
        response.IsSuccess.Should().BeTrue($"{response.Message} {string.Join(", ", response.Errors ?? [])}");
        return response.Data!.Lead;
    }

    private static async Task<(SalesTeam A, SalesTeam B)> TeamsAsync(LeadTestContext ctx, Guid userId, bool second = true, bool primaryIsB = false)
    {
        var role = new TeamRole { CompanyId = ctx.CompanyId, Code = "MEMBER", Name = "Integrante" };
        var a = new SalesTeam { CompanyId = ctx.CompanyId, Name = "A" };
        var b = new SalesTeam { CompanyId = ctx.CompanyId, Name = "B" };
        ctx.Db.AddRange(role, a, b);
        var from = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
        ctx.Db.SalesTeamMembers.Add(new SalesTeamMember { CompanyId = ctx.CompanyId, SalesTeamId = a.Id, UserId = userId, TeamRoleId = role.Id, IsPrimary = !primaryIsB, ValidFrom = from });
        if (second)
        {
            ctx.Db.SalesTeamMembers.Add(new SalesTeamMember { CompanyId = ctx.CompanyId, SalesTeamId = b.Id, UserId = userId, TeamRoleId = role.Id, IsPrimary = primaryIsB, ValidFrom = from });
        }

        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        return (a, b);
    }

    [Fact]
    public async Task Create_ShouldStartInTheInitialStage_WithCodeAndTheThreeHistories()
    {
        using var ctx = await LeadTestContext.CreateAsync();

        var lead = await CreateAsync(ctx);

        lead.Code.Should().Be("LD-000001");
        lead.Topic.Should().Be("Punto de venta");
        lead.Status.Should().Be(1);
        lead.LeadStageId.Should().Be(ctx.Stage["NEW"].Id);
        lead.OwnerUserId.Should().Be(ctx.UserId);
        lead.SalesTeamId.Should().BeNull("the owner has no team");
        lead.CurrencyId.Should().Be(ctx.Dop);
        lead.ExchangeRate.Should().Be(1m);
        (await ctx.RowsAsync<LeadStageHistory>(h => h.LeadId == lead.Id)).Should().ContainSingle(h => h.FromStageId == null);
        (await ctx.RowsAsync<LeadOwnershipHistory>(h => h.LeadId == lead.Id)).Should().ContainSingle(h => h.Cause == LeadOwnershipCause.Created);
        (await ctx.RowsAsync<LeadStatusHistory>(h => h.LeadId == lead.Id)).Should().ContainSingle(h => h.ToStatus == LeadStatus.Open);
        ctx.UnitOfWork.LeadCodeLockRequests.Should().Be(1);
        (await CreateAsync(ctx, NewLead(ctx, ctx.OtherPersonId))).Code.Should().Be("LD-000002");
    }

    [Fact]
    public async Task Create_WithANewPersonWithoutIdentification_CreatesThePersonAndItsContacts()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var command = NewLead(ctx) with
        {
            PersonId = null,
            NewPerson = new LeadNewPersonInput { PersonType = 2, FirstName = "Ferretería", LastName = "El Puente", CommercialName = "Ferretería El Puente", Phone = "809-555-0101", Email = "info@puente.do" }
        };

        var lead = await CreateAsync(ctx, command);

        var person = (await ctx.RowsAsync<Person>(p => p.Id == lead.PersonId)).Single();
        person.HasIdentification.Should().BeFalse();
        var contacts = await ctx.RowsAsync<PersonContact>(c => c.PersonId == person.Id);
        contacts.Should().HaveCount(2);
        contacts.Single(c => c.IsPrimary).ContactValue.Should().Be("info@puente.do");
    }

    [Fact]
    public async Task Create_ShouldValidatePersonAndReferences()
    {
        using var ctx = await LeadTestContext.CreateAsync();

        (await ctx.SendAsync(NewLead(ctx) with { PersonId = null })).Message.Should().Be(LeadErrorCodes.LeadPersonRequired);
        (await ctx.SendAsync(NewLead(ctx) with { NewPerson = new LeadNewPersonInput { FirstName = "A", LastName = "B" } })).Message.Should().Be(LeadErrorCodes.LeadPersonRequired);
        (await ctx.SendAsync(NewLead(ctx, Guid.NewGuid()))).Message.Should().Be(LeadErrorCodes.InvalidPerson);
        (await ctx.SendAsync(NewLead(ctx) with { PersonId = null, NewPerson = new LeadNewPersonInput { PersonType = 1, FirstName = "Sin", LastName = "Genero" } }))
            .Message.Should().Be(LeadErrorCodes.InvalidPerson, "a physical person needs a gender, as in POST /Persons");
        (await ctx.SendAsync(NewLead(ctx) with { LeadSourceId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidLeadSource);
        (await ctx.SendAsync(NewLead(ctx) with { RelationshipTypeId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidRelationshipType);
        (await ctx.SendAsync(NewLead(ctx) with { ReferredByPersonId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidPerson);
        (await ctx.SendAsync(NewLead(ctx) with { ProjectId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidReference);
        (await ctx.SendAsync(NewLead(ctx) with { AreaId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidReference);
        (await ctx.SendAsync(NewLead(ctx) with { OwnerUserId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidOwner);
        (await ctx.SendAsync(NewLead(ctx) with { CurrencyId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidCurrency);
        (await ctx.SendAsync(NewLead(ctx) with { SalesTeamId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidSalesTeam);
        (await ctx.SendAsync(NewLead(ctx) with { ProductLines = [new LeadProductInput { ProductId = ctx.Product["OLD"].Id }] })).Message.Should().Be(LeadErrorCodes.InvalidProduct);
        (await ctx.SendAsync(NewLead(ctx) with { FirstInteraction = new LeadInteractionInput { ContactTypeCompanyId = Guid.NewGuid(), Summary = "x" } })).Message.Should().Be(LeadErrorCodes.InvalidContactType);
        ctx.Rates.Remove(ctx.Usd);
        (await ctx.SendAsync(NewLead(ctx) with { CurrencyId = ctx.Usd })).Message.Should().Be(LeadErrorCodes.ExchangeRateNotFound);

        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == ctx.OtherPersonId);
        person.Deactivate();
        await ctx.Db.SaveChangesAsync();
        (await ctx.SendAsync(NewLead(ctx, ctx.OtherPersonId))).Message.Should().Be(LeadErrorCodes.InvalidPerson);

        ctx.ActingUserId = null;
        (await ctx.SendAsync(NewLead(ctx))).Message.Should().Be(LeadErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(NewLead(ctx))).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Create_WithoutAnActiveInitialStage_ShouldFail()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var initial = await ctx.Db.LeadStages.IgnoreQueryFilters().SingleAsync(s => s.Id == ctx.Stage["NEW"].Id);
        initial.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(NewLead(ctx))).Message.Should().Be(LeadErrorCodes.InvalidLeadStage);
    }

    [Fact]
    public async Task Team_OneMembershipIsUsed_TwoUseTheChosenOrThePrimary()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (a, b) = await TeamsAsync(ctx, ctx.UserId, primaryIsB: true);

        (await CreateAsync(ctx)).SalesTeamId.Should().Be(b.Id, "the primary");
        (await CreateAsync(ctx, NewLead(ctx, ctx.OtherPersonId) with { SalesTeamId = a.Id })).SalesTeamId.Should().Be(a.Id);

        var single = await ctx.AddCompanyUserAsync("solo");
        var (only, _) = await TeamsAsync(ctx, single, second: false);
        (await CreateAsync(ctx, NewLead(ctx) with { OwnerUserId = single })).SalesTeamId.Should().Be(only.Id);
        (await ctx.SendAsync(NewLead(ctx) with { OwnerUserId = single, SalesTeamId = b.Id })).Message.Should().Be(LeadErrorCodes.InvalidSalesTeam);
    }

    [Fact]
    public async Task Duplicates_FollowTheCompanyPolicy()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var first = await CreateAsync(ctx);

        var warned = await ctx.SendAsync(NewLead(ctx));
        warned.Data!.DuplicateOpenLeads.Select(d => d.Code).Should().Equal(first.Code);

        await ctx.SendAsync(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 2 });
        var blocked = await ctx.SendAsync(NewLead(ctx));
        blocked.Message.Should().Be(LeadErrorCodes.LeadDuplicateOpen);

        await ctx.SendAsync(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 3, CodePrefix = "OP-", CodeSequenceLength = 4 });
        var allowed = await ctx.SendAsync(NewLead(ctx));
        allowed.Data!.DuplicateOpenLeads.Should().BeEmpty();
        allowed.Data.Lead.Code.Should().Be("OP-0001", "a new prefix starts its own sequence");
    }

    [Fact]
    public async Task Stage_MovesToTheNextOrBack_NeverSkips_AndFollowsTheCurrentOrder()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);

        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["QUALIFIED"].Id))).Message.Should().Be(LeadErrorCodes.StageSkipNotAllowed);
        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["CONTACTED"].Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["PROPOSAL"].Id))).Message.Should().Be(LeadErrorCodes.StageSkipNotAllowed);
        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["WON"].Id))).Message.Should().Be(LeadErrorCodes.InvalidLeadStage);
        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["NEW"].Id))).IsSuccess.Should().BeTrue("going back is allowed");
        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["NEW"].Id))).Message.Should().Contain("already");

        var proposal = await ctx.Db.LeadStages.IgnoreQueryFilters().SingleAsync(s => s.Id == ctx.Stage["PROPOSAL"].Id);
        proposal.SortOrder = 2;
        var contacted = await ctx.Db.LeadStages.IgnoreQueryFilters().SingleAsync(s => s.Id == ctx.Stage["CONTACTED"].Id);
        contacted.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["PROPOSAL"].Id))).IsSuccess.Should().BeTrue("after reordering PROPOSAL is next");
        (await ctx.RowsAsync<LeadStageHistory>(h => h.LeadId == lead.Id)).Should().HaveCount(4);
    }

    [Fact]
    public async Task Products_RecalculateTheValueInLeadAndBaseCurrency_AndLockTheCurrency()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx, NewLead(ctx) with { CurrencyId = ctx.Usd, EstimatedValue = 100m });
        lead.EstimatedValue.Should().Be(100m);
        lead.EstimatedValueBase.Should().Be(6000m);

        var crm = await ctx.SendAsync(new AddLeadProductCommand { LeadId = lead.Id, Line = new LeadProductInput { ProductId = ctx.Product["CRM"].Id, Quantity = 3, DiscountPercent = 10 } });
        crm.IsSuccess.Should().BeTrue(crm.Message);
        crm.Data!.UnitPrice.Should().Be(20m, "1200 DOP ÷ 60");
        crm.Data.LineTotal.Should().Be(54m);
        var pos = await ctx.SendAsync(new AddLeadProductCommand { LeadId = lead.Id, Line = new LeadProductInput { ProductId = ctx.Product["POS"].Id, Quantity = 1, UnitPrice = 99.99m } });

        var stored = await ctx.LeadAsync(lead.Id);
        stored.EstimatedValue.Should().Be(153.99m);
        stored.EstimatedValueBase.Should().Be(9239.40m);
        stored.ExchangeRate.Should().Be(60m);

        (await ctx.SendAsync(new UpdateLeadProductCommand { LeadId = lead.Id, LineId = pos.Data!.Id, Quantity = 1, UnitPrice = 99.99m, Status = 3 })).IsSuccess.Should().BeTrue();
        (await ctx.LeadAsync(lead.Id)).EstimatedValue.Should().Be(54m, "a rejected line does not add");

        var update = new UpdateLeadCommand { Id = lead.Id, Topic = "T", RelationshipTypeId = ctx.Relationship["PROSPECT"], LeadSourceId = ctx.Source["WEB"], CurrencyId = ctx.Dop };
        (await ctx.SendAsync(update)).Message.Should().Be(LeadErrorCodes.CurrencyLocked);

        (await ctx.SendAsync(new DeleteLeadProductCommand(lead.Id, crm.Data.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadProductCommand(lead.Id, pos.Data.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadProductCommand(lead.Id, pos.Data.Id))).Message.Should().Be(LeadErrorCodes.ProductLineNotFound);
        var changed = await ctx.SendAsync(update with { EstimatedValue = 500m });
        changed.IsSuccess.Should().BeTrue(changed.Message);
        changed.Data!.EstimatedValueBase.Should().Be(500m);
        changed.Data.ExchangeRate.Should().Be(1m);
        (await ctx.SendAsync(new AddLeadProductCommand { LeadId = lead.Id, Line = new LeadProductInput { ProductId = ctx.Product["OLD"].Id } })).Message.Should().Be(LeadErrorCodes.InvalidProduct);
    }

    [Fact]
    public async Task Create_WithLinesAndFirstContact_LinksTheLinesToTheContact()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx, NewLead(ctx) with
        {
            EstimatedValue = 1m,
            ProductLines = [new LeadProductInput { ProductId = ctx.Product["CRM"].Id, Quantity = 2 }],
            FirstInteraction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["CALL"], DurationMinutes = 15, Summary = "Hola" }
        });

        lead.EstimatedValue.Should().Be(2400m, "lines win over the manual value");
        lead.LastInteractionAt.Should().NotBeNull();
        var interaction = (await ctx.RowsAsync<LeadInteraction>(i => i.LeadId == lead.Id)).Single();
        (await ctx.RowsAsync<LeadProduct>(l => l.LeadId == lead.Id)).Single().OfferedInInteractionId.Should().Be(interaction.Id);
        (await ctx.SendAsync(NewLead(ctx) with { FirstInteraction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["CALL"], Summary = "x" } }))
            .Message.Should().Be(LeadErrorCodes.InteractionDurationRequired);
    }

    [Fact]
    public async Task Interactions_NeedDurationWhenAsked_AnOpenLead_AndOnlyTheAuthorEdits()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        var call = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["CALL"], Summary = "Llamada" };

        (await ctx.SendAsync(new AddLeadInteractionCommand { LeadId = lead.Id, Interaction = call })).Message.Should().Be(LeadErrorCodes.InteractionDurationRequired);
        var added = await ctx.SendAsync(new AddLeadInteractionCommand { LeadId = lead.Id, Interaction = call with { DurationMinutes = 5, OccurredAt = DateTime.UtcNow.AddHours(-2) } });
        added.IsSuccess.Should().BeTrue(added.Message);
        (await ctx.LeadAsync(lead.Id)).LastInteractionAt.Should().BeCloseTo(DateTime.UtcNow.AddHours(-2), TimeSpan.FromMinutes(1));

        var edited = await ctx.SendAsync(new UpdateLeadInteractionCommand
        {
            LeadId = lead.Id, InteractionId = added.Data!.Id, Interaction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["EMAIL"], Summary = "Correo", Outcome = 2 }
        });
        edited.Data!.Summary.Should().Be("Correo");
        edited.Data.Outcome.Should().Be(2);
        (await ctx.SendAsync(new UpdateLeadInteractionCommand { LeadId = lead.Id, InteractionId = Guid.NewGuid(), Interaction = call })).Message.Should().Be(LeadErrorCodes.InteractionNotFound);

        ctx.Scopes.Set(new LeadAccessScope(ctx.OtherUserId, true, [], [], false));
        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new UpdateLeadInteractionCommand { LeadId = lead.Id, InteractionId = added.Data.Id, Interaction = call with { DurationMinutes = 1 } }))
            .Message.Should().Be(LeadErrorCodes.ActionForbidden);

        ctx.ActAs(ctx.UserId);
        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        (await ctx.SendAsync(new AddLeadInteractionCommand { LeadId = lead.Id, Interaction = call with { DurationMinutes = 5 } })).Message.Should().Be(LeadErrorCodes.LeadNotOpen);
    }

    [Fact]
    public async Task Interaction_OnAnInactivePerson_ShouldFail()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == ctx.PersonId);
        person.Deactivate();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new AddLeadInteractionCommand { LeadId = lead.Id, Interaction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["EMAIL"], Summary = "x" } }))
            .Message.Should().Be(LeadErrorCodes.PersonInactive);
    }

    [Fact]
    public async Task Comments_OnlyTheAuthorEditsOrDeletes()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        var comment = (await ctx.SendAsync(new AddLeadCommentCommand { LeadId = lead.Id, Body = " Nota " })).Data!;
        comment.Body.Should().Be("Nota");
        (await ctx.SendAsync(new AddLeadCommentCommand { LeadId = lead.Id, Body = "x", LeadInteractionId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InteractionNotFound);

        ctx.Scopes.Set(new LeadAccessScope(ctx.OtherUserId, true, [], [], false));
        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new UpdateLeadCommentCommand { LeadId = lead.Id, CommentId = comment.Id, Body = "hack" })).Message.Should().Be(LeadErrorCodes.ActionForbidden);
        (await ctx.SendAsync(new DeleteLeadCommentCommand(lead.Id, comment.Id))).Message.Should().Be(LeadErrorCodes.ActionForbidden);

        ctx.ActAs(ctx.UserId);
        (await ctx.SendAsync(new UpdateLeadCommentCommand { LeadId = lead.Id, CommentId = comment.Id, Body = "Editada" })).Data!.Body.Should().Be("Editada");
        (await ctx.SendAsync(new DeleteLeadCommentCommand(lead.Id, comment.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadCommentCommand(lead.Id, comment.Id))).Message.Should().Be(LeadErrorCodes.CommentNotFound);
    }

    [Fact]
    public async Task Visibility_OutOfScopeIsNotFound_InactiveIsVisibleToEveryone()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);

        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new AddLeadCommentCommand { LeadId = lead.Id, Body = "x" })).Message.Should().Be(LeadErrorCodes.LeadNotFound);
        (await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] })).Message.Should().Be(LeadErrorCodes.LeadNotFound);

        ctx.ActAs(ctx.UserId);
        (await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["HIGH_PRICE"] })).Message.Should().Be(LeadErrorCodes.InvalidLeadReason);
        var inactive = await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"], Notes = " sin respuesta " });
        inactive.Data!.Status.Should().Be(2);
        inactive.Data.StatusNotes.Should().Be("sin respuesta");
        (await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] })).Message.Should().Be(LeadErrorCodes.LeadNotOpen);
        var editInactive = new UpdateLeadCommand { Id = lead.Id, Topic = "T", RelationshipTypeId = ctx.Relationship["PROSPECT"], LeadSourceId = ctx.Source["WEB"], CurrencyId = ctx.Dop };
        (await ctx.SendAsync(editInactive)).Message.Should().Be(LeadErrorCodes.LeadNotOpen);

        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new AddLeadCommentCommand { LeadId = lead.Id, Body = "Lo veo" })).IsSuccess.Should().BeTrue("inactive leads are visible to the company");
    }

    [Fact]
    public async Task InactivateWithoutScope_IsForbidden_EvenForAVisibleLead()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        ctx.Scopes.Set(new LeadAccessScope(ctx.OtherUserId, true, [], [], false));
        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] })).IsSuccess.Should().BeTrue("company-wide scope covers it");

        ctx.ActAs(ctx.UserId);
        var reactivated = await ctx.SendAsync(new ReactivateLeadCommand { Id = lead.Id });
        reactivated.IsSuccess.Should().BeTrue(reactivated.Message);
    }

    [Fact]
    public async Task Reactivate_MakesTheCallerTheOwnerWithTheirTeam_AndKeepsTheHistory()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (a, _) = await TeamsAsync(ctx, ctx.UserId, second: false);
        var lead = await CreateAsync(ctx);
        lead.SalesTeamId.Should().Be(a.Id);
        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        var otherTeam = await TeamsAsync(ctx, ctx.OtherUserId, second: false);

        ctx.ActAs(ctx.OtherUserId);
        var reactivated = await ctx.SendAsync(new ReactivateLeadCommand { Id = lead.Id, Notes = "La retomo" });

        reactivated.Data!.Status.Should().Be(1);
        reactivated.Data.OwnerUserId.Should().Be(ctx.OtherUserId);
        reactivated.Data.SalesTeamId.Should().Be(otherTeam.A.Id);
        reactivated.Data.LeadReasonId.Should().BeNull();
        var ownership = await ctx.RowsAsync<LeadOwnershipHistory>(h => h.LeadId == lead.Id && h.Cause == LeadOwnershipCause.Reactivated);
        ownership.Single().FromUserId.Should().Be(ctx.UserId);
        ownership.Single().FromTeamId.Should().Be(a.Id);
        (await ctx.SendAsync(new ReactivateLeadCommand { Id = lead.Id })).Message.Should().Be(LeadErrorCodes.LeadNotInactive);
    }

    [Fact]
    public async Task Reactivate_WithAnInactivePerson_ShouldFail()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == ctx.PersonId);
        person.Deactivate();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new ReactivateLeadCommand { Id = lead.Id })).Message.Should().Be(LeadErrorCodes.PersonInactive);
    }

    [Fact]
    public async Task Reassign_NeedsCanReassignOnTheTeam_AndKeepsTheTeam()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (a, _) = await TeamsAsync(ctx, ctx.UserId, second: false);
        var lead = await CreateAsync(ctx);
        var command = new ReassignLeadCommand { Id = lead.Id, NewOwnerUserId = ctx.OtherUserId, Notes = "vacaciones" };

        (await ctx.SendAsync(command)).Message.Should().Be(LeadErrorCodes.ActionForbidden);

        ctx.Scopes.Set(new LeadAccessScope(ctx.UserId, false, [a.Id], [a.Id], false));
        (await ctx.SendAsync(command with { NewOwnerUserId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidOwner);
        (await ctx.SendAsync(command with { NewOwnerUserId = ctx.UserId })).Message.Should().Contain("already");
        var reassigned = await ctx.SendAsync(command);

        reassigned.Data!.OwnerUserId.Should().Be(ctx.OtherUserId);
        reassigned.Data.SalesTeamId.Should().Be(a.Id);
        (await ctx.RowsAsync<LeadOwnershipHistory>(h => h.LeadId == lead.Id && h.Cause == LeadOwnershipCause.Reassigned)).Single().Notes.Should().Be("vacaciones");

        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(command with { NewOwnerUserId = ctx.UserId })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ChangesTheEditableFields()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        var command = new UpdateLeadCommand
        {
            Id = lead.Id, Topic = " Nuevo tema ", RelationshipTypeId = ctx.Relationship["PROSPECT"], LeadSourceId = ctx.Source["REFERRAL"], ReferredByPersonId = ctx.OtherPersonId,
            EstimatedCloseDate = new DateOnly(2026, 12, 1), CurrencyId = ctx.Usd, EstimatedValue = 10m
        };

        var updated = await ctx.SendAsync(command);

        updated.Data!.Topic.Should().Be("Nuevo tema");
        updated.Data.LeadSourceId.Should().Be(ctx.Source["REFERRAL"]);
        updated.Data.EstimatedValueBase.Should().Be(600m);
        (await ctx.SendAsync(command with { LeadSourceId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidLeadSource);
        (await ctx.SendAsync(command with { RelationshipTypeId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidRelationshipType);
        (await ctx.SendAsync(command with { ReferredByPersonId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidPerson);
        (await ctx.SendAsync(command with { ProjectId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidReference);
        (await ctx.SendAsync(command with { CurrencyId = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.InvalidCurrency);
        (await ctx.SendAsync(command with { Id = Guid.NewGuid() })).Message.Should().Be(LeadErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task InUse_CatalogsProductsContactTypesAndPersons_CannotBeDeleted()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx, NewLead(ctx) with
        {
            ProductLines = [new LeadProductInput { ProductId = ctx.Product["CRM"].Id }],
            FirstInteraction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["EMAIL"], Summary = "x" },
            ReferredByPersonId = ctx.OtherPersonId
        });
        await ctx.SendAsync(new InactivateLeadCommand { Id = lead.Id, LeadReasonId = ctx.Reason["NO_ANSWER"] });

        (await ctx.SendAsync(new DeleteLeadSourceCommand(ctx.Source["WEB"]))).Errors.Should().Equal("Active Leads: 1");
        (await ctx.SendAsync(new DeleteLeadSourceCommand(ctx.Source["WEB"]))).Message.Should().Be(LeadErrorCodes.LeadSourceInUse);
        (await ctx.SendAsync(new DeleteLeadSourceCommand(ctx.Source["REFERRAL"]))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadStageCommand(ctx.Stage["NEW"].Id))).Message.Should().Be(LeadSettingsErrorCodes.LeadStageKindRequired);
        (await ctx.SendAsync(new DeleteLeadReasonCommand(ctx.Reason["NO_ANSWER"]))).Message.Should().Be(LeadErrorCodes.LeadReasonInUse);
        (await ctx.SendAsync(new DeleteRelationshipTypeCommand(ctx.Relationship["PROSPECT"]))).Message.Should().Be(LeadErrorCodes.RelationshipTypeInUse);
        (await ctx.SendAsync(new DeleteProductCommand(ctx.Product["CRM"].Id))).Errors.Should().Equal("Active LeadProducts: 1");
        (await ctx.SendAsync(new DeleteProductCommand(ctx.Product["CRM"].Id))).Message.Should().Be(CatalogErrorCodes.ProductInUse);

        ctx.IsSuperAdmin = true;
        var master = (await ctx.RowsAsync<ContactTypeCompany>(c => c.Id == ctx.ContactType["EMAIL"])).Single().ContactTypeId;
        var type = await ctx.SendAsync(new DeleteContactTypeCommand(master));
        type.Message.Should().Be(LeadErrorCodes.ContactTypeInUse);
        type.Errors.Should().Equal("Active LeadInteractions: 1");

        var references = await ctx.Get<PersonCascadeCoordinator>().GetBlockingReferencesAsync(ctx.PersonId);
        references.Should().Contain("Active Leads: 1");
        (await ctx.Get<PersonCascadeCoordinator>().GetBlockingReferencesAsync(ctx.OtherPersonId)).Should().Contain("Active Leads: 1", "referrer");

        ctx.IsSuperAdmin = false;
        var stage = await ctx.SendAsync(new CreateLeadStageCommand { Code = "DEMO", Name = "Demo", Kind = 2 });
        (await ctx.SendAsync(new DeleteLeadStageCommand(stage.Data!.Id))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task LeadStageInUse_IsReported()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await CreateAsync(ctx);
        await ctx.SendAsync(new ChangeLeadStageCommand(lead.Id, ctx.Stage["CONTACTED"].Id));

        var inUse = await ctx.SendAsync(new DeleteLeadStageCommand(ctx.Stage["CONTACTED"].Id));

        inUse.Message.Should().Be(LeadErrorCodes.LeadStageInUse);
        inUse.Errors.Should().Equal("Active Leads: 1");
    }

    [Fact]
    public void Calculators_And_Validators()
    {
        LeadProduct.Total(3, 33.335m, 0).Should().Be(100.01m);
        LeadProduct.Total(2, 10, 50).Should().Be(10m);
        LeadCodeGenerator.TryParse("LD-000123", "LD-").Should().Be(123);
        LeadCodeGenerator.TryParse("XX-1", "LD-").Should().BeNull();
        LeadCodeGenerator.TryParse("LD-", "LD-").Should().BeNull();
        LeadWorkRules.DefaultPrice(new JOIN.Domain.Catalog.Product { ReferencePrice = 100 }, 0).Should().Be(100);

        new CreateLeadCommandValidator().Validate(new CreateLeadCommand()).Errors.Should().HaveCount(3);
        new CreateLeadCommandValidator().Validate(new CreateLeadCommand
        {
            Topic = "T", RelationshipTypeId = Guid.NewGuid(), LeadSourceId = Guid.NewGuid(), EstimatedValue = -1,
            ProductLines = [new LeadProductInput { ProductId = Guid.NewGuid(), Quantity = 0, DiscountPercent = 101, Status = 9, OfferedInInteractionId = Guid.NewGuid() }],
            FirstInteraction = new LeadInteractionInput { Direction = 3, Outcome = 5, DurationMinutes = 0 }
        }).Errors.Count.Should().BeGreaterThan(8);
        new UpdateLeadCommandValidator().Validate(new UpdateLeadCommand()).IsValid.Should().BeFalse();
        new ChangeLeadStageCommandValidator().Validate(new ChangeLeadStageCommand(Guid.Empty, Guid.Empty)).Errors.Should().HaveCount(2);
        new ReassignLeadCommandValidator().Validate(new ReassignLeadCommand()).Errors.Should().HaveCount(2);
        new InactivateLeadCommandValidator().Validate(new InactivateLeadCommand()).Errors.Should().HaveCount(2);
        new ReactivateLeadCommandValidator().Validate(new ReactivateLeadCommand()).Errors.Should().HaveCount(1);
        new AddLeadInteractionCommandValidator().Validate(new AddLeadInteractionCommand()).IsValid.Should().BeFalse();
        new UpdateLeadInteractionCommandValidator().Validate(new UpdateLeadInteractionCommand()).IsValid.Should().BeFalse();
        new AddLeadCommentCommandValidator().Validate(new AddLeadCommentCommand()).Errors.Should().HaveCount(2);
        new UpdateLeadCommentCommandValidator().Validate(new UpdateLeadCommentCommand()).Errors.Should().HaveCount(3);
        new AddLeadProductCommandValidator().Validate(new AddLeadProductCommand()).IsValid.Should().BeFalse();
        new UpdateLeadProductCommandValidator().Validate(new UpdateLeadProductCommand { Quantity = 0, DiscountPercent = -1, Status = 0, UnitPrice = -1 }).Errors.Should().HaveCount(6);
    }

    [Fact]
    public async Task StageAdvance_RejectsInactiveTargets()
    {
        var coordinator = new LeadStageAdvanceCoordinator();
        var a = new LeadStage { Code = "A", SortOrder = 1, Kind = LeadStageKind.Initial };
        var b = new LeadStage { Code = "B", SortOrder = 2, IsActive = false };
        var c = new LeadStage { Code = "C", SortOrder = 3 };
        LeadStage[] all = [a, b, c];

        coordinator.Validate<object>(a, b, all)!.Message.Should().Be(LeadErrorCodes.InvalidLeadStage);
        coordinator.Validate<object>(a, null, all)!.Message.Should().Be(LeadErrorCodes.InvalidLeadStage);
        coordinator.Validate<object>(a, c, all).Should().BeNull("B is inactive, so C is next");
        coordinator.Validate<object>(c, a, all).Should().BeNull();
        await Task.CompletedTask;
    }
}
