using FluentAssertions;
using JOIN.Application.Common.Settings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ConvertPersonPendingToLead;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// SPEC 65: the next action of a contact (module gate and schedulable contact types) and converting a pending person
/// into a lead. The calendar event itself is covered by the integration tests against PostgreSQL.
/// </summary>
public sealed class LeadCalendarAndPendingTests
{
    private static async Task<Guid> LeadAsync(LeadTestContext ctx, Guid? personId = null) =>
        (await ctx.SendAsync(new CreateLeadCommand
        {
            PersonId = personId ?? ctx.PersonId, Topic = "T", RelationshipTypeId = ctx.Relationship["PROSPECT"], LeadSourceId = ctx.Source["WEB"]
        })).Data!.Lead.Id;

    private static LeadNextActionInput Next(Guid contactTypeCompanyId) => new()
    {
        ContactTypeCompanyId = contactTypeCompanyId, StartUtc = DateTime.UtcNow.AddDays(1), EndUtc = DateTime.UtcNow.AddDays(1).AddHours(1)
    };

    private static LeadNextActionScheduler Scheduler(LeadTestContext ctx, bool enforce, bool calendarEnabled)
    {
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(x => x.GetModuleAccessAsync(It.IsAny<Guid>(), LeadNextActionScheduler.CalendarResource, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModuleAccessResult(calendarEnabled, "Calendar"));
        return new LeadNextActionScheduler(ctx.UnitOfWork, Options.Create(new ModuleEnforcementSettings { EnforceCompanyModules = enforce }), permissions.Object,
            ctx.CurrentUser.Object, ctx.Get<CalendarEventCommandProcessor>());
    }

    [Fact]
    public async Task NextAction_WithoutTheCalendarModule_IsModuleNotEnabled()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await ctx.LeadAsync(await LeadAsync(ctx));

        var (eventId, error) = await Scheduler(ctx, enforce: true, calendarEnabled: false).ScheduleAsync<object>(lead, Next(ctx.ContactType["CALL"]), CancellationToken.None);

        eventId.Should().BeNull();
        error!.Message.Should().Be(LeadErrorCodes.ModuleNotEnabled);
        error.Errors.Should().Equal("Calendar");

        ctx.IsSuperAdmin = true;
        (await Scheduler(ctx, enforce: true, calendarEnabled: false).ScheduleAsync<object>(lead, Next(ctx.ContactType["CALL"]), CancellationToken.None))
            .Error!.Message.Should().Be(LeadErrorCodes.ContactTypeNotSchedulable, "the SuperAdmin passes the module gate");
    }

    [Fact]
    public async Task NextAction_WithATypeThatDoesNotSchedule_Is400()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var lead = await ctx.LeadAsync(await LeadAsync(ctx));

        (await Scheduler(ctx, enforce: true, calendarEnabled: true).ScheduleAsync<object>(lead, Next(ctx.ContactType["CALL"]), CancellationToken.None))
            .Error!.Message.Should().Be(LeadErrorCodes.ContactTypeNotSchedulable);
        (await Scheduler(ctx, enforce: false, calendarEnabled: false).ScheduleAsync<object>(lead, Next(Guid.NewGuid()), CancellationToken.None))
            .Error!.Message.Should().Be(LeadErrorCodes.ContactTypeNotSchedulable);
    }

    [Fact]
    public async Task AddInteraction_WithANonSchedulableNextAction_DoesNotRegisterTheContact()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var leadId = await LeadAsync(ctx);

        var response = await ctx.SendAsync(new AddLeadInteractionCommand
        {
            LeadId = leadId,
            Interaction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["EMAIL"], Summary = "x", NextAction = Next(ctx.ContactType["EMAIL"]) }
        });

        response.Message.Should().Be(LeadErrorCodes.ContactTypeNotSchedulable);
        (await ctx.RowsAsync<LeadInteraction>(i => i.LeadId == leadId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Convert_CreatesAnOpenLeadWithOriginPending_ResolvesThePending_AndCreatesNoCustomer()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var pendingSource = new LeadSource { CompanyId = ctx.CompanyId, Code = "PENDING", Name = "Persona pendiente" };
        var pending = new PersonPendingConfirmation
        {
            CompanyId = ctx.CompanyId, PersonId = ctx.OtherPersonId, Reason = "Nuevo por WhatsApp", CommunicationChannelId = Guid.NewGuid(), ChannelIdentifier = "+18095550101", RequestedUtc = DateTime.UtcNow
        };
        ctx.Db.AddRange(pendingSource, pending);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        var command = new ConvertPersonPendingToLeadCommand { PendingConfirmationId = pending.Id, Topic = "Pidió información", RelationshipTypeId = ctx.Relationship["PROSPECT"], Notes = " vino por WhatsApp " };

        var converted = await ctx.SendAsync(command);

        converted.IsSuccess.Should().BeTrue($"{converted.Message} {string.Join(",", converted.Errors ?? [])}");
        var lead = await ctx.LeadAsync(converted.Data!.LeadId);
        lead.Status.Should().Be(LeadStatus.Open);
        lead.LeadSourceId.Should().Be(pendingSource.Id);
        lead.OwnerUserId.Should().Be(ctx.UserId);
        var resolved = (await ctx.RowsAsync<PersonPendingConfirmation>(p => p.Id == pending.Id)).Single();
        resolved.Status.Should().Be(PersonPendingConfirmationStatus.ConvertedToLead);
        resolved.LeadId.Should().Be(lead.Id);
        resolved.ResolvedByUserId.Should().Be(ctx.UserId);
        resolved.ResolutionNotes.Should().Be("vino por WhatsApp");
        (await ctx.RowsAsync<Customer>(c => c.PersonId == ctx.OtherPersonId)).Should().BeEmpty();

        (await ctx.SendAsync(command)).Message.Should().Be(ConvertPersonPendingToLeadCommandHandler.NotPending);
    }

    [Fact]
    public async Task Convert_FailsWithoutThePendingSource_ForLeadErrors_AndForMissingContext()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var pending = new PersonPendingConfirmation
        {
            CompanyId = ctx.CompanyId, PersonId = ctx.OtherPersonId, Reason = "x", CommunicationChannelId = Guid.NewGuid(), ChannelIdentifier = "a@b.c", RequestedUtc = DateTime.UtcNow
        };
        ctx.Db.Add(pending);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        var command = new ConvertPersonPendingToLeadCommand { PendingConfirmationId = pending.Id, Topic = "T", RelationshipTypeId = ctx.Relationship["PROSPECT"] };

        (await ctx.SendAsync(command)).Message.Should().Be(LeadErrorCodes.InvalidLeadSource);

        ctx.Db.Add(new LeadSource { CompanyId = ctx.CompanyId, Code = "PENDING", Name = "Persona pendiente" });
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        await LeadAsync(ctx, ctx.OtherPersonId);
        await ctx.SendAsync(new SaveLeadCompanySettingsCommand { DuplicateOpenLeadPolicy = 2 });
        (await ctx.SendAsync(command)).Message.Should().Be(LeadErrorCodes.LeadDuplicateOpen);
        (await ctx.SendAsync(command with { PendingConfirmationId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.PersonPendingNotFound);

        ctx.ActingUserId = null;
        (await ctx.SendAsync(command)).Message.Should().Be(CalendarErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(command)).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        new ConvertPersonPendingToLeadCommandValidator().Validate(new ConvertPersonPendingToLeadCommand()).Errors.Should().HaveCount(3);
        new LeadNextActionInputValidator().Validate(new LeadNextActionInput { StartUtc = DateTime.UtcNow, EndUtc = DateTime.UtcNow.AddHours(-1) }).Errors.Should().HaveCount(2);
    }

    [Fact]
    public async Task Events_ApplyVisibility_AndReadTheLeadEvents()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CompanyId).Returns(companyId);
        user.SetupGet(x => x.UserId).Returns(userId.ToString());
        var guard = new LeadAccessGuard(new LeadTestContext.FakeScopes(), user.Object);

        var hidden = new FakeSqlConnectionFactory();
        hidden.SetResults(FakeResultSet.Empty("Status"));
        (await new GetLeadEventsQueryHandler(hidden, user.Object, guard).Handle(new GetLeadEventsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.LeadNotFound);

        var visible = new FakeSqlConnectionFactory();
        visible.SetResults(
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Status"] = 1, ["OwnerUserId"] = userId, ["SalesTeamId"] = null }),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Title"] = "Demo", ["StatusCode"] = "PENDING" }));
        var events = await new GetLeadEventsQueryHandler(visible, user.Object, guard).Handle(new GetLeadEventsQuery(Guid.NewGuid()), CancellationToken.None);
        events.Data!.Single().Title.Should().Be("Demo");
        visible.LastCommandText.Should().Contain("e.LeadId = @LeadId").And.Contain("calendar.activitycompanies");

        user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await new GetLeadEventsQueryHandler(hidden, user.Object, guard).Handle(new GetLeadEventsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }
}
