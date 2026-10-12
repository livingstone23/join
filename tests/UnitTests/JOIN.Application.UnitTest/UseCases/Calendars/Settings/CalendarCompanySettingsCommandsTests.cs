using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.CreateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.DeleteCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Commands.UpdateCalendarCompanyNonWorkingDay;
using JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.CreateCalendarEvent;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.DeleteUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.SetDefaultUserCalendar;
using JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Settings;

/// <summary>
/// SPEC 44 — company configuration, holidays, user configuration, non-working periods, user calendars and
/// provisioning.
/// </summary>
public sealed class CalendarCompanySettingsCommandsTests
{
    private static CreateCalendarCompanyCommand CompanyCommand(CalendarTestContext ctx, string zone = "America/Managua") => new()
    {
        DefaultActivityUserId = ctx.OwnerId,
        TimeZone = zone,
        BusinessHoursStart = new TimeOnly(8, 0),
        BusinessHoursEnd = new TimeOnly(17, 0)
    };

    // ---------------------------------------------------------------- CalendarCompanies

    [Fact]
    public async Task CreateCompany_ShouldDefaultTheRescheduleStatusAndProvisionUsers()
    {
        using var ctx = await CalendarTestContext.CreateAsync(configureCompany: false);

        var response = await ctx.SendAsync(CompanyCommand(ctx));

        response.IsSuccess.Should().BeTrue(string.Join(" ", response.Errors ?? []));
        response.Data!.RescheduledByOthersStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Rescheduled]);
        response.Data.DefaultCalendarName.Should().Be("Mi calendario");
        response.Data.ModuleEnabled.Should().BeTrue();
        response.Message.Should().Contain("Users provisioned: 2");
        (await ctx.Db.CalendarUserConfigurations.IgnoreQueryFilters().CountAsync()).Should().Be(2);
        (await ctx.Db.UserCalendars.IgnoreQueryFilters().Select(c => c.Name).Distinct().ToListAsync()).Should().Equal("Mi calendario");

        (await ctx.SendAsync(CompanyCommand(ctx))).Message.Should().Be(CalendarErrorCodes.CompanyAlreadyConfigured);
    }

    [Fact]
    public async Task CreateCompany_WithoutModule_ShouldNotProvision()
    {
        using var ctx = await CalendarTestContext.CreateAsync(configureCompany: false, moduleActive: false);

        var response = await ctx.SendAsync(CompanyCommand(ctx));

        response.Data!.ModuleEnabled.Should().BeFalse();
        (await ctx.Db.CalendarUserConfigurations.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateCompany_ShouldValidateZoneHoursUserAndStatus()
    {
        using var ctx = await CalendarTestContext.CreateAsync(configureCompany: false);

        (await ctx.SendAsync(CompanyCommand(ctx, "Mars/Olympus"))).Message.Should().Be(CalendarErrorCodes.InvalidTimeZone);
        (await ctx.SendAsync(CompanyCommand(ctx) with { BusinessHoursEnd = new TimeOnly(7, 0) })).Message.Should().Be(CalendarErrorCodes.InvalidBusinessHours);
        (await ctx.SendAsync(CompanyCommand(ctx) with { BusinessHoursStart = null })).Message.Should().Be(CalendarErrorCodes.InvalidBusinessHours);
        (await ctx.SendAsync(CompanyCommand(ctx) with { DefaultActivityUserId = ctx.StrangerId })).Message.Should().Be(CalendarErrorCodes.DefaultUserNotInCompany);
        (await ctx.SendAsync(CompanyCommand(ctx) with { RescheduledByOthersStatusCompanyId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);

        var open24 = await ctx.SendAsync(CompanyCommand(ctx) with { IsOpen24Hours = true, BusinessHoursEnd = null, DefaultCalendarName = " Agenda " });
        open24.Data!.BusinessHoursStart.Should().BeNull("a 24-hour company ignores the hours");
        open24.Data.DefaultCalendarName.Should().Be("Agenda");

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(CompanyCommand(ctx))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task UpdateCompany_ShouldValidateUserHoursAndWarnAboutTheZone()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var config = await ctx.Db.CalendarUserConfigurations.SingleAsync(c => c.UserId == ctx.OtherId);
        config.BusinessHoursStart = new TimeOnly(8, 0);
        config.BusinessHoursEnd = new TimeOnly(12, 0);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        UpdateCalendarCompanyCommand Update(string zone, int start, int end) => new()
        {
            DefaultActivityUserId = ctx.OwnerId, TimeZone = zone, BusinessHoursStart = new TimeOnly(start, 0), BusinessHoursEnd = new TimeOnly(end, 0)
        };

        var narrowed = await ctx.SendAsync(Update("America/Managua", 9, 17));
        narrowed.Message.Should().Be(CalendarErrorCodes.UserHoursOutOfCompanyRange);
        narrowed.Errors.Should().ContainSingle(e => e.Contains("other@t.local"));

        await ctx.SendAsync(new CreateCalendarEventCommand { CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Reminder], Title = "x", Start = DateTimeOffset.UtcNow.AddDays(3) });
        var moved = await ctx.SendAsync(Update("America/Guatemala", 7, 18));
        moved.IsSuccess.Should().BeTrue();
        moved.Data!.Warnings.Should().ContainSingle();
        moved.Data.TimeZone.Should().Be("America/Guatemala");

        (await ctx.SendAsync(Update("Nowhere/Zone", 7, 18))).Message.Should().Be(CalendarErrorCodes.InvalidTimeZone);
        (await ctx.SendAsync(Update("America/Managua", 7, 18) with { DefaultActivityUserId = ctx.StrangerId })).Message.Should().Be(CalendarErrorCodes.DefaultUserNotInCompany);
        (await ctx.SendAsync(Update("America/Managua", 7, 18) with { RescheduledByOthersStatusCompanyId = Guid.NewGuid() })).Message.Should().Be(CalendarErrorCodes.StatusNotEnabled);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Update("America/Managua", 7, 18))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task UpdateCompany_WithoutConfiguration_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync(configureCompany: false);

        (await ctx.SendAsync(new UpdateCalendarCompanyCommand { DefaultActivityUserId = ctx.OwnerId, TimeZone = "America/Managua", IsOpen24Hours = true }))
            .Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    [Fact]
    public async Task SyncCatalogs_ShouldReturnTheSeederResult()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.CatalogSeeder.Setup(s => s.SeedCalendarCatalogsForCompanyAsync(ctx.CompanyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CalendarCatalogSyncResult(2, 1));

        var response = await ctx.SendAsync(new SyncCalendarCatalogsCommand());

        response.Data.Should().Be(new JOIN.Application.DTO.Calendars.CalendarCatalogSyncDto(2, 1));
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new SyncCalendarCatalogsCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task ProvisionUsers_ShouldProvisionPendingMembersOnly()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Db.UserCompanies.Add(new UserCompany { UserId = ctx.StrangerId, CompanyId = ctx.CompanyId });
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var response = await ctx.SendAsync(new ProvisionCalendarUsersCommand());

        response.Data.Should().ContainSingle(u => u.UserId == ctx.StrangerId && u.Email == "stranger@t.local");
        (await ctx.SendAsync(new ProvisionCalendarUsersCommand())).Data.Should().BeEmpty("it is idempotent");

        using var unconfigured = await CalendarTestContext.CreateAsync(configureCompany: false);
        (await unconfigured.SendAsync(new ProvisionCalendarUsersCommand())).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        unconfigured.ActingCompanyId = Guid.Empty;
        (await unconfigured.SendAsync(new ProvisionCalendarUsersCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Provisioner_ShouldBeIdempotentAndDoNothingWithoutModuleOrConfiguration()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var provisioner = ctx.Get<CalendarUserProvisioner>();

        (await provisioner.EnsureUserCalendarAsync(ctx.CompanyId, ctx.OwnerId, CancellationToken.None)).Should().BeFalse("already provisioned");
        (await provisioner.EnsureUserCalendarAsync(ctx.CompanyId, ctx.StrangerId, CancellationToken.None)).Should().BeTrue();
        await ctx.UnitOfWork.SaveChangesAsync();
        var config = await ctx.Db.CalendarUserConfigurations.IgnoreQueryFilters().SingleAsync(c => c.UserId == ctx.StrangerId);
        config.WorkDays.Should().Be(CalendarWorkDays.Weekdays);
        config.BusinessHoursStart.Should().BeNull("the effective hours are the company's");

        using var noModule = await CalendarTestContext.CreateAsync(moduleActive: false);
        (await noModule.Get<CalendarUserProvisioner>().EnsureUserCalendarAsync(noModule.CompanyId, noModule.StrangerId, CancellationToken.None)).Should().BeFalse();

        using var noConfig = await CalendarTestContext.CreateAsync(configureCompany: false);
        (await noConfig.Get<CalendarUserProvisioner>().EnsureUserCalendarAsync(noConfig.CompanyId, noConfig.OwnerId, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task Provisioner_WithoutTheSystemModule_ShouldReportInactive()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var module = await ctx.Db.SystemModules.SingleAsync();
        module.MarkAsDeleted();
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.Get<CalendarUserProvisioner>().IsModuleActiveAsync(ctx.CompanyId)).Should().BeFalse();
    }

    // ---------------------------------------------------------------- holidays

    [Fact]
    public async Task Holidays_CreateUpdateDelete_ShouldRejectDuplicateDates()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var created = await ctx.SendAsync(new CreateCalendarCompanyNonWorkingDayCommand { Date = new DateOnly(2027, 9, 15), Name = " Independencia ", IsRecurringYearly = true });
        created.Data!.Name.Should().Be("Independencia");
        created.Data.OccursOn.Should().Be(new DateOnly(2027, 9, 15));
        (await ctx.SendAsync(new CreateCalendarCompanyNonWorkingDayCommand { Date = new DateOnly(2027, 9, 15), Name = "x" })).Message.Should().Be(CalendarErrorCodes.NonWorkingDayDuplicate);

        var other = await ctx.SendAsync(new CreateCalendarCompanyNonWorkingDayCommand { Date = new DateOnly(2027, 9, 14), Name = "San Jacinto" });
        (await ctx.SendAsync(new UpdateCalendarCompanyNonWorkingDayCommand { Id = other.Data!.Id, Date = new DateOnly(2027, 9, 15), Name = "x" }))
            .Message.Should().Be(CalendarErrorCodes.NonWorkingDayDuplicate);
        (await ctx.SendAsync(new UpdateCalendarCompanyNonWorkingDayCommand { Id = other.Data.Id, Date = new DateOnly(2027, 9, 13), Name = "Movido" }))
            .Data!.Date.Should().Be(new DateOnly(2027, 9, 13));
        (await ctx.SendAsync(new UpdateCalendarCompanyNonWorkingDayCommand { Id = Guid.NewGuid(), Date = new DateOnly(2027, 1, 1), Name = "x" }))
            .Message.Should().Be(CalendarErrorCodes.NonWorkingDayNotFound);

        (await ctx.SendAsync(new DeleteCalendarCompanyNonWorkingDayCommand(other.Data.Id))).Data.Should().BeTrue();
        (await ctx.SendAsync(new DeleteCalendarCompanyNonWorkingDayCommand(other.Data.Id))).Message.Should().Be(CalendarErrorCodes.NonWorkingDayNotFound);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new CreateCalendarCompanyNonWorkingDayCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCalendarCompanyNonWorkingDayCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCalendarCompanyNonWorkingDayCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    // ---------------------------------------------------------------- user configuration

    [Fact]
    public async Task UpdateMyConfiguration_ShouldOnlyChangePreferences()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        var response = await ctx.SendAsync(new UpdateMyCalendarConfigurationCommand { DefaultView = CalendarViewType.Month, WeekStartsOn = DayOfWeek.Sunday });

        response.Data!.DefaultView.Should().Be((int)CalendarViewType.Month);
        response.Data.WeekStartsOn.Should().Be(0);
        response.Data.EffectiveBusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        response.Data.DefaultCalendarName.Should().Be("Mi calendario");

        ctx.ActingUserId = ctx.StrangerId;
        (await ctx.SendAsync(new UpdateMyCalendarConfigurationCommand())).Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
        ctx.ActingUserId = null;
        (await ctx.SendAsync(new UpdateMyCalendarConfigurationCommand())).Message.Should().Be(CalendarErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new UpdateMyCalendarConfigurationCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);

        using var unconfigured = await CalendarTestContext.CreateAsync(configureCompany: false);
        (await unconfigured.SendAsync(new UpdateMyCalendarConfigurationCommand())).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    [Fact]
    public async Task UpdateUserConfiguration_ShouldKeepHoursInsideTheCompanyRange()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        UpdateUserCalendarConfigurationCommand Command(int start, int end) => new()
        {
            UserId = ctx.OtherId, BusinessHoursStart = new TimeOnly(start, 0), BusinessHoursEnd = new TimeOnly(end, 0),
            WorkDays = CalendarWorkDays.Monday | CalendarWorkDays.Saturday
        };

        (await ctx.SendAsync(Command(7, 12))).Message.Should().Be(CalendarErrorCodes.UserHoursOutOfCompanyRange);
        var ok = await ctx.SendAsync(Command(9, 13));
        ok.Data!.EffectiveBusinessHoursEnd.Should().Be(new TimeOnly(13, 0));
        ok.Data.WorkDays.Should().Be(66);

        (await ctx.SendAsync(Command(9, 13) with { UserId = ctx.StrangerId })).Message.Should().Be(CalendarErrorCodes.UserConfigurationNotFound);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Command(9, 13))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);

        using var open24 = await CalendarTestContext.CreateAsync(open24Hours: true);
        (await open24.SendAsync(new UpdateUserCalendarConfigurationCommand { UserId = open24.OtherId, BusinessHoursStart = new TimeOnly(1, 0), BusinessHoursEnd = new TimeOnly(5, 0) }))
            .IsSuccess.Should().BeTrue("any range of the day is valid in a 24-hour company");

        using var unconfigured = await CalendarTestContext.CreateAsync(configureCompany: false);
        (await unconfigured.SendAsync(new UpdateUserCalendarConfigurationCommand { UserId = unconfigured.OtherId })).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    // ---------------------------------------------------------------- non-working periods

    [Fact]
    public async Task MyPeriods_ShouldRejectOverlapsLongRangesAndListAffectedActivities()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        await ctx.SendAsync(new CreateCalendarEventCommand { CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Meeting], Title = "Afectada", Start = Local(2027, 3, 2, 9) });

        var created = await ctx.SendAsync(new CreateMyNonWorkingPeriodCommand { StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 5), Notes = " playa " });

        created.Data!.Period.Notes.Should().Be("playa");
        created.Data.Period.UserName.Should().Be("Owner User");
        created.Data.AffectedEvents.Should().ContainSingle(e => e.Title == "Afectada");
        (await ctx.SendAsync(new CreateMyNonWorkingPeriodCommand { StartDate = new DateOnly(2027, 3, 5), EndDate = new DateOnly(2027, 3, 6) }))
            .Message.Should().Be(CalendarErrorCodes.NonWorkingPeriodOverlap);
        (await ctx.SendAsync(new CreateMyNonWorkingPeriodCommand { StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2028, 1, 2) }))
            .Message.Should().Be(CalendarErrorCodes.InvalidEventTimes);

        ctx.ActingUserId = ctx.OtherId;
        (await ctx.SendAsync(new DeleteMyNonWorkingPeriodCommand(created.Data.Period.Id))).Message.Should().Be(CalendarErrorCodes.NonWorkingPeriodNotFound);
        ctx.ActingUserId = ctx.OwnerId;
        (await ctx.SendAsync(new DeleteMyNonWorkingPeriodCommand(created.Data.Period.Id))).Data.Should().BeTrue();

        ctx.ActingUserId = null;
        (await ctx.SendAsync(new CreateMyNonWorkingPeriodCommand())).Message.Should().Be(CalendarErrorCodes.UserRequired);
        (await ctx.SendAsync(new DeleteMyNonWorkingPeriodCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new CreateMyNonWorkingPeriodCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteMyNonWorkingPeriodCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task UserPeriods_ShouldBeManagedForMembersOnly()
    {
        using var ctx = await CalendarTestContext.CreateAsync();

        (await ctx.SendAsync(new CreateUserNonWorkingPeriodCommand { UserId = ctx.StrangerId, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1) }))
            .Message.Should().Be(CalendarErrorCodes.UserNotInCompany);

        var created = await ctx.SendAsync(new CreateUserNonWorkingPeriodCommand { UserId = ctx.OtherId, StartDate = new DateOnly(2027, 3, 1), EndDate = new DateOnly(2027, 3, 1), Reason = CalendarNonWorkingReason.Absence });
        created.Data!.Period.Reason.Should().Be((int)CalendarNonWorkingReason.Absence);

        var updated = await ctx.SendAsync(new UpdateUserNonWorkingPeriodCommand { Id = created.Data.Period.Id, StartDate = new DateOnly(2027, 3, 2), EndDate = new DateOnly(2027, 3, 3), Reason = CalendarNonWorkingReason.Leave });
        updated.Data!.Period.UserId.Should().Be(ctx.OtherId);
        updated.Data.Period.StartDate.Should().Be(new DateOnly(2027, 3, 2));
        (await ctx.SendAsync(new UpdateUserNonWorkingPeriodCommand { Id = Guid.NewGuid(), StartDate = new DateOnly(2027, 3, 2), EndDate = new DateOnly(2027, 3, 3) }))
            .Message.Should().Be(CalendarErrorCodes.NonWorkingPeriodNotFound);

        (await ctx.SendAsync(new DeleteUserNonWorkingPeriodCommand(created.Data.Period.Id))).Data.Should().BeTrue();

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new CreateUserNonWorkingPeriodCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateUserNonWorkingPeriodCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteUserNonWorkingPeriodCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    // ---------------------------------------------------------------- user calendars

    [Fact]
    public async Task UserCalendars_DefaultCannotBeRetiredUntilAnotherIsDefault()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var defaultId = ctx.DefaultCalendar[ctx.OwnerId];

        var second = await ctx.SendAsync(new CreateUserCalendarCommand { Name = " Ventas ", Color = "#43A047", Description = " d " });
        second.Data!.Name.Should().Be("Ventas");
        second.Data.IsDefault.Should().BeFalse();
        (await ctx.SendAsync(new CreateUserCalendarCommand { Name = "Ventas", Color = "#43A047" })).Message.Should().Be(CalendarErrorCodes.CalendarNameDuplicate);

        (await ctx.SendAsync(new DeleteUserCalendarCommand(defaultId))).Message.Should().Be(CalendarErrorCodes.CalendarIsDefault);
        (await ctx.SendAsync(new UpdateUserCalendarCommand { Id = defaultId, Name = "Mi calendario", Color = "#000000", IsActive = false }))
            .Message.Should().Be(CalendarErrorCodes.CalendarIsDefault);

        (await ctx.SendAsync(new SetDefaultUserCalendarCommand(second.Data.Id))).Data!.IsDefault.Should().BeTrue();
        (await ctx.SendAsync(new DeleteUserCalendarCommand(defaultId))).Data.Should().BeTrue();
    }

    [Fact]
    public async Task UserCalendars_WithUpcomingActivities_CannotBeRetired()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var second = (await ctx.SendAsync(new CreateUserCalendarCommand { Name = "Ventas", Color = "#43A047" })).Data!;
        await ctx.SendAsync(new CreateCalendarEventCommand
        {
            CalendarId = second.Id, CalendarActivityCompanyId = ctx.Activity[CalendarCodes.Activities.Reminder], Title = "x", Start = DateTimeOffset.UtcNow.AddDays(2)
        });

        var deactivate = await ctx.SendAsync(new UpdateUserCalendarCommand { Id = second.Id, Name = "Ventas", Color = "#000000", IsActive = false });
        deactivate.Message.Should().Be(CalendarErrorCodes.CalendarHasActiveEvents);
        deactivate.Errors!.Single().Should().Contain("1 upcoming");

        var renamed = await ctx.SendAsync(new UpdateUserCalendarCommand { Id = second.Id, Name = "Comercial", Color = "#000000", IsActive = true });
        renamed.Data!.Name.Should().Be("Comercial");
        (await ctx.SendAsync(new UpdateUserCalendarCommand { Id = second.Id, Name = "Mi calendario", Color = "#000000" })).Message.Should().Be(CalendarErrorCodes.CalendarNameDuplicate);
    }

    [Fact]
    public async Task UserCalendars_ShouldHideOtherUsersCalendarsAndRequireProvisioning()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var foreign = ctx.DefaultCalendar[ctx.OtherId];

        (await ctx.SendAsync(new UpdateUserCalendarCommand { Id = foreign, Name = "x", Color = "#000000" })).Message.Should().Be(CalendarErrorCodes.CalendarNotFound);
        (await ctx.SendAsync(new DeleteUserCalendarCommand(foreign))).Message.Should().Be(CalendarErrorCodes.CalendarNotFound);
        (await ctx.SendAsync(new SetDefaultUserCalendarCommand(foreign))).Message.Should().Be(CalendarErrorCodes.CalendarNotFound);

        var inactive = new UserCalendar { CompanyId = ctx.CompanyId, OwnerUserId = ctx.OwnerId, Name = "Old", Color = "#000000", IsActive = false };
        ctx.Db.UserCalendars.Add(inactive);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        (await ctx.SendAsync(new SetDefaultUserCalendarCommand(inactive.Id))).Message.Should().Be(CalendarErrorCodes.CalendarInactive);

        ctx.ActingUserId = ctx.StrangerId;
        (await ctx.SendAsync(new CreateUserCalendarCommand { Name = "x", Color = "#000000" })).Message.Should().Be(CalendarErrorCodes.UserHasNoCalendar);
        ctx.ActingUserId = null;
        (await ctx.SendAsync(new CreateUserCalendarCommand())).Message.Should().Be(CalendarErrorCodes.UserRequired);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new UpdateUserCalendarCommand())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteUserCalendarCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new SetDefaultUserCalendarCommand(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task DefaultCoordinator_WithoutConfiguration_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var calendar = new UserCalendar { CompanyId = ctx.CompanyId, OwnerUserId = ctx.StrangerId, Name = "x", Color = "#000000", IsActive = true };

        var response = await ctx.Get<CalendarUserDefaultCoordinator>().SetDefaultAsync(calendar);

        response.Message.Should().Be(CalendarErrorCodes.UserConfigurationNotFound);
    }
}
