using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.EnableCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.UpdateCalendarActivityCompany;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.EnableCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.DeleteCalendarStatus;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.UpdateCalendarStatus;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using Microsoft.EntityFrameworkCore;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Settings;

/// <summary>
/// SPEC 44 — global catalogs (<c>CalendarCatalogs</c>) and company catalogs (<c>CalendarSettings</c>).
/// </summary>
public sealed class CalendarCatalogCommandsTests
{
    private static CreateCalendarActivityCommand NewActivity(string code) => new()
    {
        Code = code, Name = " Visita ", Color = "#123456", DefaultDurationMinutes = 45, Icon = " car ", Description = " ", SortOrder = 9
    };

    [Fact]
    public async Task Activities_CreateUpdateDelete_ShouldFollowTheRules()
    {
        using var ctx = await CreateAsync();

        var created = await ctx.SendAsync(NewActivity(" visit "));
        created.Data!.Code.Should().Be("VISIT");
        created.Data.Name.Should().Be("Visita");
        created.Data.Description.Should().BeNull();
        created.Data.Icon.Should().Be("car");

        (await ctx.SendAsync(NewActivity("VISIT"))).Message.Should().Be(CalendarErrorCodes.ActivityCodeDuplicate);

        var updated = await ctx.SendAsync(new UpdateCalendarActivityCommand
        {
            Id = created.Data.Id, Name = "Visita técnica", Color = "#654321", DefaultDurationMinutes = 90, AllowsOverlap = true, IsActive = false
        });
        updated.Data!.Name.Should().Be("Visita técnica");
        updated.Data.Code.Should().Be("VISIT");
        updated.Data.IsActive.Should().BeFalse();

        (await ctx.SendAsync(new UpdateCalendarActivityCommand { Id = Guid.NewGuid(), Name = "x", Color = "#000000", DefaultDurationMinutes = 5 }))
            .Message.Should().Be(CalendarErrorCodes.ActivityNotFound);

        (await ctx.SendAsync(new DeleteCalendarActivityCommand(created.Data.Id))).Data.Should().BeTrue();
        (await ctx.SendAsync(new DeleteCalendarActivityCommand(created.Data.Id))).Message.Should().Be(CalendarErrorCodes.ActivityNotFound);
    }

    [Fact]
    public async Task Activities_Delete_ShouldBlockSystemCodesAndTypesEnabledInACompany()
    {
        using var ctx = await CreateAsync();

        (await ctx.SendAsync(new DeleteCalendarActivityCommand(ctx.GlobalActivity[CalendarCodes.Activities.PersonReview])))
            .Message.Should().Be(CalendarErrorCodes.ActivityRequiredBySystem);
        (await ctx.SendAsync(new DeleteCalendarActivityCommand(ctx.GlobalActivity[CalendarCodes.Activities.Meeting])))
            .Message.Should().Be(CalendarErrorCodes.ActivityInUse);
    }

    [Fact]
    public async Task Statuses_CreateUpdateDelete_ShouldProtectSystemStatuses()
    {
        using var ctx = await CreateAsync();

        var created = await ctx.SendAsync(new CreateCalendarStatusCommand { Code = "on_hold", Name = "En espera", Color = "#ABCDEF", Description = "d" });
        created.Data!.Code.Should().Be("ON_HOLD");
        (await ctx.SendAsync(new CreateCalendarStatusCommand { Code = "ON_HOLD", Name = "x", Color = "#ABCDEF" })).Message.Should().Be(CalendarErrorCodes.StatusCodeDuplicate);

        var updated = await ctx.SendAsync(new UpdateCalendarStatusCommand { Id = created.Data.Id, Name = "Espera", Color = "#000000", BlocksTime = false, IsActive = false });
        updated.Data!.BlocksTime.Should().BeFalse();

        (await ctx.SendAsync(new UpdateCalendarStatusCommand { Id = ctx.GlobalStatus[CalendarCodes.Statuses.Pending], Name = "x", Color = "#000000", IsActive = false }))
            .Message.Should().Be(CalendarErrorCodes.StatusRequiredBySystem);
        (await ctx.SendAsync(new UpdateCalendarStatusCommand { Id = Guid.NewGuid(), Name = "x", Color = "#000000" }))
            .Message.Should().Be(CalendarErrorCodes.StatusNotFound);

        (await ctx.SendAsync(new DeleteCalendarStatusCommand(ctx.GlobalStatus[CalendarCodes.Statuses.Cancelled]))).Message.Should().Be(CalendarErrorCodes.StatusRequiredBySystem);
        (await ctx.SendAsync(new DeleteCalendarStatusCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.StatusNotFound);

        ctx.Db.CalendarStatusCompanies.Add(new CalendarStatusCompany { CompanyId = ctx.CompanyId, CalendarStatusId = created.Data.Id, Name = "x", Color = "#000000" });
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await ctx.SendAsync(new DeleteCalendarStatusCommand(created.Data.Id))).Message.Should().Be(CalendarErrorCodes.StatusInUse);
    }

    [Fact]
    public async Task Statuses_Delete_WithoutCompanies_ShouldSucceed()
    {
        using var ctx = await CreateAsync();
        var created = await ctx.SendAsync(new CreateCalendarStatusCommand { Code = "TEMP", Name = "Temp", Color = "#ABCDEF" });

        (await ctx.SendAsync(new DeleteCalendarStatusCommand(created.Data!.Id))).Data.Should().BeTrue();
    }

    [Fact]
    public async Task Catalogs_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(NewActivity("X"))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCalendarActivityCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCalendarActivityCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new CreateCalendarStatusCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCalendarStatusCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCalendarStatusCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new EnableCalendarActivityCompanyCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCalendarActivityCompanyCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCalendarActivityCompanyCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new EnableCalendarStatusCompanyCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCalendarStatusCompanyCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task ActivityCompanies_Enable_ShouldCopyValuesAndDefaultToPending()
    {
        using var ctx = await CreateAsync();
        var global = (await ctx.SendAsync(NewActivity("VISIT"))).Data!;

        var enabled = await ctx.SendAsync(new EnableCalendarActivityCompanyCommand { CalendarActivityId = global.Id });

        enabled.Data!.Name.Should().Be("Visita");
        enabled.Data.DefaultDurationMinutes.Should().Be(45);
        enabled.Data.InitialCalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Pending]);
        enabled.Data.IsUsable.Should().BeTrue();
        (await ctx.SendAsync(new EnableCalendarActivityCompanyCommand { CalendarActivityId = global.Id })).Message.Should().Be(CalendarErrorCodes.ActivityAlreadyEnabled);
        (await ctx.SendAsync(new EnableCalendarActivityCompanyCommand { CalendarActivityId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.ActivityNotFound);
    }

    [Fact]
    public async Task ActivityCompanies_EnableOrUpdateWithInvalidInitialStatus_ShouldFail()
    {
        using var ctx = await CreateAsync();
        var global = (await ctx.SendAsync(NewActivity("VISIT"))).Data!;

        (await ctx.SendAsync(new EnableCalendarActivityCompanyCommand { CalendarActivityId = global.Id, InitialCalendarStatusCompanyId = Guid.NewGuid() }))
            .Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
        (await ctx.SendAsync(new UpdateCalendarActivityCompanyCommand
        {
            Id = ctx.Activity[CalendarCodes.Activities.Meeting], Name = "x", Color = "#000000", DefaultDurationMinutes = 30, InitialCalendarStatusCompanyId = Guid.NewGuid()
        })).Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
        (await ctx.SendAsync(new UpdateCalendarActivityCompanyCommand { Id = Guid.NewGuid(), Name = "x", Color = "#000000", DefaultDurationMinutes = 30 }))
            .Message.Should().Be(CalendarErrorCodes.ActivityNotFound);
    }

    [Fact]
    public async Task ActivityCompanies_Update_ShouldChangeTheEffectiveValuesUsedByNewActivities()
    {
        using var ctx = await CreateAsync();

        var updated = await ctx.SendAsync(new UpdateCalendarActivityCompanyCommand
        {
            Id = ctx.Activity[CalendarCodes.Activities.Meeting], Name = "Reunión corta", Color = "#000000", DefaultDurationMinutes = 20,
            AllowsOverlap = false, RequiresBusinessHours = true, InitialCalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed], IsActive = true
        });
        updated.Data!.InitialStatusName.Should().Be("confirmed");

        var created = await ctx.SendAsync(new CreateCalendarEventCommand
        {
            CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x", Start = Local(2027, 3, 1, 9)
        });
        created.Data!.EndUtc.Should().Be(Utc(2027, 3, 1, 9, 20));
        created.Data.Status.Code.Should().Be(CalendarCodes.Statuses.Confirmed);
    }

    [Fact]
    public async Task ActivityCompanies_Delete_ShouldDeactivateWhenInUseAndDeleteOtherwise()
    {
        using var ctx = await CreateAsync();
        await ctx.SendAsync(new CreateCalendarEventCommand { CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x", Start = Local(2027, 3, 1, 9) });

        var inUse = await ctx.SendAsync(new DeleteCalendarActivityCompanyCommand(ctx.Activity[CalendarCodes.Activities.Meeting]));
        inUse.IsSuccess.Should().BeTrue();
        inUse.Data.Should().BeFalse();
        inUse.Errors.Should().Contain(CalendarErrorCodes.ActivityInUse);
        (await ctx.Db.CalendarActivityCompanies.IgnoreQueryFilters().SingleAsync(a => a.Id == ctx.Activity[CalendarCodes.Activities.Meeting])).IsActive.Should().BeFalse();

        (await ctx.SendAsync(new DeleteCalendarActivityCompanyCommand(ctx.Activity[CalendarCodes.Activities.Task]))).Data.Should().BeTrue();
        (await ctx.SendAsync(new DeleteCalendarActivityCompanyCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.ActivityNotFound);
    }

    [Fact]
    public async Task StatusCompanies_EnableUpdateAndRemovalRules()
    {
        using var ctx = await CreateAsync();
        var global = (await ctx.SendAsync(new CreateCalendarStatusCommand { Code = "ON_HOLD", Name = "En espera", Color = "#ABCDEF" })).Data!;

        var enabled = await ctx.SendAsync(new EnableCalendarStatusCompanyCommand { CalendarStatusId = global.Id });
        enabled.Data!.Code.Should().Be("ON_HOLD");
        (await ctx.SendAsync(new EnableCalendarStatusCompanyCommand { CalendarStatusId = global.Id })).Message.Should().Be(CalendarErrorCodes.StatusAlreadyEnabled);
        (await ctx.SendAsync(new EnableCalendarStatusCompanyCommand { CalendarStatusId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.StatusNotFound);

        var renamed = await ctx.SendAsync(new UpdateCalendarStatusCompanyCommand { Id = enabled.Data.Id, Name = "Espera", Color = "#111111", IsActive = false });
        renamed.Data!.Name.Should().Be("Espera");
        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(enabled.Data.Id))).Data.Should().BeTrue();

        UpdateCalendarStatusCompanyCommand Deactivate(string code) => new() { Id = ctx.Status[code], Name = "x", Color = "#000000", IsActive = false };
        (await ctx.SendAsync(Deactivate(CalendarCodes.Statuses.Pending))).Message.Should().Be(CalendarErrorCodes.StatusRequiredBySystem);
        (await ctx.SendAsync(Deactivate(CalendarCodes.Statuses.Rescheduled))).Message.Should().Be(CalendarErrorCodes.StatusInUseAsReschedule);
        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(ctx.Status[CalendarCodes.Statuses.Cancelled]))).Message.Should().Be(CalendarErrorCodes.StatusRequiredBySystem);
        (await ctx.SendAsync(new UpdateCalendarStatusCompanyCommand { Id = Guid.NewGuid(), Name = "x", Color = "#000000" })).Message.Should().Be(CalendarErrorCodes.StatusNotFound);
        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.StatusNotFound);
    }

    [Fact]
    public async Task StatusCompanies_InitialOrWithActivities_ShouldNotBeRemovable()
    {
        using var ctx = await CreateAsync();
        var activity = await ctx.Db.CalendarActivityCompanies.SingleAsync(a => a.Id == ctx.Activity[CalendarCodes.Activities.Task]);
        activity.InitialCalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Confirmed];
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(ctx.Status[CalendarCodes.Statuses.Confirmed]))).Message.Should().Be(CalendarErrorCodes.StatusInUseAsInitial);

        await ctx.SendAsync(new CreateCalendarEventCommand { CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "x", Start = Local(2027, 3, 1, 9) });
        var meeting = await ctx.Db.CalendarActivityCompanies.SingleAsync(a => a.Id == ctx.Activity[CalendarCodes.Activities.Meeting]);
        var completedEvent = await ctx.Db.CalendarEvents.SingleAsync();
        completedEvent.CalendarStatusCompanyId = ctx.Status[CalendarCodes.Statuses.Completed];
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new DeleteCalendarStatusCompanyCommand(ctx.Status[CalendarCodes.Statuses.Completed]))).Message.Should().Be(CalendarErrorCodes.StatusInUse);
        meeting.Should().NotBeNull();
    }
}
