using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.IntakeTestData;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonPendingConfirmations;

/// <summary>
/// SPEC 45 — closing a pending confirmation (confirm, discard, link to a customer): only from <c>Pending</c>, with
/// who and when, and the review activity completed with its log row; the person is never changed.
/// </summary>
public sealed class PersonPendingConfirmationCommandsTests
{
    private sealed record Setup(CalendarTestContext Ctx, IntakeTestData Data, Guid PendingId, Guid PersonId, Guid ReviewEventId);

    private static async Task<Setup> WithPendingAsync()
    {
        var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(NoPerson());
        var registered = await ctx.SendAsync(new RegisterIntakePersonCommand(
            PersonType.Physical, "Luis", "Pérez", null, data.IdentificationTypeId, "002", null, "+50588886666", null,
            "Cita", "WHATSAPP", "+50588886666", "wamid.P"));
        registered.IsSuccess.Should().BeTrue(registered.Message + string.Join(" ", registered.Errors ?? []));
        ctx.ActingUserId = ctx.OwnerId;
        ctx.Db.ChangeTracker.Clear();
        return new Setup(ctx, data, registered.Data!.PendingConfirmationId!.Value, registered.Data.PersonId, registered.Data.ReviewEventId!.Value);
    }

    private static async Task<PersonPendingConfirmation> ReloadPendingAsync(CalendarTestContext ctx, Guid id)
    {
        ctx.Db.ChangeTracker.Clear();
        return await ctx.Db.PersonPendingConfirmations.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
    }

    [Fact]
    public async Task Confirm_ShouldCloseThePendingAndCompleteTheReview()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;

        var response = await ctx.SendAsync(new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Confirmed, " Datos revisados "));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        response.Data.Should().Be(new JOIN.Application.DTO.Admin.ResolvePersonPendingConfirmationDto(setup.PendingId, 2, setup.ReviewEventId));
        var pending = await ReloadPendingAsync(ctx, setup.PendingId);
        pending.Status.Should().Be(PersonPendingConfirmationStatus.Confirmed);
        pending.ResolvedByUserId.Should().Be(ctx.OwnerId);
        pending.ResolvedUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        pending.ResolutionNotes.Should().Be("Datos revisados");

        (await ctx.ReloadEventAsync(setup.ReviewEventId)).CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Completed]);
        var log = (await ctx.LogsAsync(setup.ReviewEventId)).Single(l => l.Action == CalendarEventLogAction.StatusChanged);
        log.Notes.Should().Be(PersonPendingReviewCloser.ClosingNotes);
        log.ActorUserId.Should().Be(ctx.OwnerId);
        log.ChannelId.Should().Be(ctx.WebChannelId);

        (await ctx.SendAsync(new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Discarded, "x")))
            .Message.Should().Be(CalendarErrorCodes.PersonPendingAlreadyResolved);
    }

    [Fact]
    public async Task Discard_ShouldKeepThePersonActive()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;

        var response = await ctx.SendAsync(new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Discarded, "Spam"));

        response.IsSuccess.Should().BeTrue();
        (await ReloadPendingAsync(ctx, setup.PendingId)).Status.Should().Be(PersonPendingConfirmationStatus.Discarded);
        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == setup.PersonId);
        person.IsActive.Should().BeTrue();
        person.GcRecord.Should().Be(0);
    }

    [Fact]
    public async Task Link_ShouldRequireACustomerOfTheSamePerson()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;
        var own = Customer.Create(ctx.CompanyId, setup.PersonId, ctx.OwnerId, "C-001", Guid.NewGuid());
        var foreign = Customer.Create(ctx.CompanyId, setup.Data.PersonId, ctx.OwnerId, "C-002", Guid.NewGuid());
        ctx.Db.Customers.AddRange(own, foreign);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new LinkCustomerToPersonPendingConfirmationCommand(setup.PendingId, foreign.Id))).Message.Should().Be(CalendarErrorCodes.PersonPendingInvalidCustomer);
        (await ctx.SendAsync(new LinkCustomerToPersonPendingConfirmationCommand(setup.PendingId, Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.PersonPendingInvalidCustomer);

        var response = await ctx.SendAsync(new LinkCustomerToPersonPendingConfirmationCommand(setup.PendingId, own.Id));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        var pending = await ReloadPendingAsync(ctx, setup.PendingId);
        pending.Status.Should().Be(PersonPendingConfirmationStatus.LinkedToCustomer);
        pending.CustomerId.Should().Be(own.Id);
        pending.ResolutionNotes.Should().Be("Vinculada al cliente C-001");
        (await ctx.ReloadEventAsync(setup.ReviewEventId)).CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Completed]);
        (await ctx.SendAsync(new LinkCustomerToPersonPendingConfirmationCommand(setup.PendingId, own.Id))).Message.Should().Be(CalendarErrorCodes.PersonPendingAlreadyResolved);
    }

    [Fact]
    public async Task ReviewAlreadyClosed_ShouldNotBeTouched()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;
        var review = await ctx.ReloadEventAsync(setup.ReviewEventId);
        review.CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Cancelled];
        ctx.Db.CalendarEvents.Update(review);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Confirmed, null))).IsSuccess.Should().BeTrue();

        (await ctx.ReloadEventAsync(setup.ReviewEventId)).CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Cancelled]);
        (await ctx.LogsAsync(setup.ReviewEventId)).Should().NotContain(l => l.Action == CalendarEventLogAction.StatusChanged);
    }

    [Fact]
    public async Task Closer_ShouldIgnoreMissingReviewsAndMissingCompletedStatus()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;
        var closer = ctx.Get<PersonPendingReviewCloser>();
        var pending = await ReloadPendingAsync(ctx, setup.PendingId);

        (await closer.CloseAsync(new PersonPendingConfirmation { CompanyId = ctx.CompanyId }, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await closer.CloseAsync(new PersonPendingConfirmation { CompanyId = ctx.CompanyId, ReviewEventId = Guid.NewGuid() }, CancellationToken.None)).IsSuccess.Should().BeTrue();

        var completed = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Completed]);
        completed.GcRecord = 1;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await closer.CloseAsync(pending, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await ctx.ReloadEventAsync(setup.ReviewEventId)).CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Pending]);
    }

    [Fact]
    public async Task Closer_ShouldReportAFailedStatusChange()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;
        var completed = await ctx.Db.CalendarStatusCompanies.SingleAsync(s => s.Id == ctx.Status[CalendarCodes.Statuses.Completed]);
        completed.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Confirmed, null));

        response.Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
    }

    [Fact]
    public async Task ContextErrors_ShouldFail()
    {
        var setup = await WithPendingAsync();
        using var ctx = setup.Ctx;
        var resolve = new ResolvePersonPendingConfirmationCommand(setup.PendingId, PersonPendingConfirmationStatus.Confirmed, null);
        var link = new LinkCustomerToPersonPendingConfirmationCommand(setup.PendingId, Guid.NewGuid());

        (await ctx.SendAsync(resolve with { PendingConfirmationId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.PersonPendingNotFound);
        (await ctx.SendAsync(link with { PendingConfirmationId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.PersonPendingNotFound);

        ctx.ActingUserId = null;
        (await ctx.SendAsync(resolve)).Message.Should().Be(CalendarErrorCodes.UserRequired);
        (await ctx.SendAsync(link)).Message.Should().Be(CalendarErrorCodes.UserRequired);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(resolve)).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(link)).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }
}
