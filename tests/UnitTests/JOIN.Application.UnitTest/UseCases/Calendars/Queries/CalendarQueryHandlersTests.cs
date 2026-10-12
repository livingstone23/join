using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivities;
using JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivityById;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanies;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanyById;
using JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetUsableCalendarActivityCompanies;
using JOIN.Application.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDayById;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDays;
using JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDaysRange;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanies;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanyById;
using JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetUsableCalendarStatusCompanies;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatusById;
using JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatuses;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetCalendarUserConfigurations;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetMyCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Queries.GetUserCalendarConfiguration;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetMyNonWorkingPeriods;
using JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Queries.GetUserNonWorkingPeriods;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;

namespace JOIN.Application.UnitTest.UseCases.Calendars.Queries;

/// <summary>
/// SPEC 44 — Dapper query handlers of the settings side, over a fake connection (one batch per command): tenant and
/// soft-delete predicates, filters, pagination, mapping and error codes.
/// </summary>
public sealed class CalendarQueryHandlersTests
{
    internal static Dictionary<string, object?> Row(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    internal static FakeResultSet CompanyRow(bool open24 = false) => FakeResultSet.FromRows(Row(
        ("Id", Guid.NewGuid()), ("TimeZone", "America/Managua"), ("IsOpen24Hours", open24),
        ("BusinessHoursStart", open24 ? null : new TimeOnly(8, 0)), ("BusinessHoursEnd", open24 ? null : new TimeOnly(17, 0))));

    internal static FakeResultSet NoRows(params string[] columns) => FakeResultSet.Empty(columns.Length == 0 ? ["Id"] : columns);

    [Fact]
    public async Task GlobalCatalogs_ShouldPageFilterAndFindById()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        ctx.Connection
            .Then(FakeResultSet.FromRows(Row(("Id", id), ("Code", "MEETING"), ("Name", "Reunión"), ("Color", "#1E88E5"), ("DefaultDurationMinutes", 60), ("IsActive", true))), FakeResultSet.FromScalar(11))
            .Then(FakeResultSet.FromRows(Row(("Id", id), ("Code", "MEETING"), ("Name", "Reunión"))))
            .Then(NoRows())
            .Then(FakeResultSet.FromRows(Row(("Id", id), ("Code", "PENDING"), ("Name", "Pendiente"), ("BlocksTime", true))), FakeResultSet.FromScalar(1))
            .Then(FakeResultSet.FromRows(Row(("Id", id), ("Code", "PENDING"))))
            .Then(NoRows());

        var activities = await ctx.SendAsync(new GetCalendarActivitiesQuery(" reu ", true, 2, 5));
        activities.Data!.Items.Single().DefaultDurationMinutes.Should().Be(60);
        activities.Data.TotalCount.Should().Be(11);
        activities.Data.TotalPages.Should().Be(3);
        ctx.Connection.LastCommandText.Should().Contain("a.GcRecord = 0").And.Contain("ILIKE @Search").And.Contain("a.IsActive = @IsActive");
        ctx.Connection.CapturedParameters["Search"].Should().Be("%reu%");
        ctx.Connection.CapturedParameters["Offset"].Should().Be(5);

        (await ctx.SendAsync(new GetCalendarActivityByIdQuery(id))).Data!.Code.Should().Be("MEETING");
        (await ctx.SendAsync(new GetCalendarActivityByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.ActivityNotFound);

        var statuses = await ctx.SendAsync(new GetCalendarStatusesQuery("pen", false));
        statuses.Data!.Items.Single().BlocksTime.Should().BeTrue();
        (await ctx.SendAsync(new GetCalendarStatusByIdQuery(id))).Data!.Code.Should().Be("PENDING");
        (await ctx.SendAsync(new GetCalendarStatusByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.StatusNotFound);
    }

    [Fact]
    public async Task CompanyCatalogs_ShouldFilterByTenantAndExposeUsable()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        var activity = Row(("Id", id), ("Code", "MEETING"), ("GlobalName", "Reunión"), ("GlobalIsActive", true), ("Name", "Junta"), ("IsActive", true));
        var status = Row(("Id", id), ("Code", "PENDING"), ("GlobalName", "Pendiente"), ("GlobalIsActive", false), ("Name", "Por confirmar"), ("IsActive", true));
        ctx.Connection
            .Then(FakeResultSet.FromRows(activity), FakeResultSet.FromScalar(1))
            .Then(FakeResultSet.FromRows(activity))
            .Then(NoRows())
            .Then(FakeResultSet.FromRows(activity))
            .Then(FakeResultSet.FromRows(status), FakeResultSet.FromScalar(1))
            .Then(FakeResultSet.FromRows(status))
            .Then(NoRows())
            .Then(FakeResultSet.FromRows(status));

        var page = await ctx.SendAsync(new GetCalendarActivityCompaniesQuery("jun", true));
        page.Data!.Items.Single().IsUsable.Should().BeTrue();
        ctx.Connection.CapturedParameters["TenantId"].Should().Be(ctx.CompanyId);
        ctx.Connection.LastCommandText.Should().Contain("ac.CompanyId = @TenantId AND ac.GcRecord = 0");

        (await ctx.SendAsync(new GetCalendarActivityCompanyByIdQuery(id))).Data!.Name.Should().Be("Junta");
        (await ctx.SendAsync(new GetCalendarActivityCompanyByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.ActivityNotFound);
        (await ctx.SendAsync(new GetUsableCalendarActivityCompaniesQuery())).Data.Should().ContainSingle();

        var statuses = await ctx.SendAsync(new GetCalendarStatusCompaniesQuery("por", true));
        statuses.Data!.Items.Single().IsUsable.Should().BeFalse("the global status is inactive");
        (await ctx.SendAsync(new GetCalendarStatusCompanyByIdQuery(id))).Data!.Name.Should().Be("Por confirmar");
        (await ctx.SendAsync(new GetCalendarStatusCompanyByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.StatusNotFound);
        (await ctx.SendAsync(new GetUsableCalendarStatusCompaniesQuery())).Data.Should().ContainSingle();
        ctx.Connection.LastCommandText.Should().Contain("s.IsActive = TRUE");
    }

    [Fact]
    public async Task CurrentCompany_ShouldNeverBeNotFound()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection
            .Then(NoRows("Id", "TimeZone"), FakeResultSet.FromScalar(0), FakeResultSet.FromScalar(3))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("TimeZone", "America/Managua"), ("DefaultActivityUserName", " Owner User "),
                ("BusinessHoursStart", new TimeOnly(8, 0)))), FakeResultSet.FromScalar(1), FakeResultSet.FromScalar(0));

        var empty = await ctx.SendAsync(new GetCurrentCalendarCompanyQuery());
        empty.Data!.IsConfigured.Should().BeFalse();
        empty.Data.ModuleEnabled.Should().BeFalse();
        empty.Data.UsersWithoutCalendar.Should().Be(3);

        var configured = await ctx.SendAsync(new GetCurrentCalendarCompanyQuery());
        configured.Data!.IsConfigured.Should().BeTrue();
        configured.Data.ModuleEnabled.Should().BeTrue();
        configured.Data.DefaultActivityUserName.Should().Be("Owner User");
        configured.Data.BusinessHoursStart.Should().Be(new TimeOnly(8, 0));
    }

    [Fact]
    public async Task Holidays_ShouldResolveRecurringOnesAndPageThem()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var holidays = FakeResultSet.FromRows(
            Row(("Id", Guid.NewGuid()), ("Date", new DateOnly(2020, 9, 15)), ("Name", "Independencia"), ("IsRecurringYearly", true)),
            Row(("Id", Guid.NewGuid()), ("Date", new DateOnly(2020, 2, 29)), ("Name", "Bisiesto"), ("IsRecurringYearly", true)),
            Row(("Id", Guid.NewGuid()), ("Date", new DateOnly(2027, 4, 2)), ("Name", "Viernes Santo"), ("IsRecurringYearly", false)));
        ctx.Connection.Then(holidays).Then(holidays).Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("Date", new DateOnly(2027, 1, 1)), ("Name", "Año"))))
            .Then(NoRows());

        var page = await ctx.SendAsync(new GetCalendarCompanyNonWorkingDaysQuery(2027, 1, 1));
        page.Data!.TotalCount.Should().Be(2, "02-29 does not exist in 2027");
        page.Data.Items.Single().OccursOn.Should().Be(new DateOnly(2027, 4, 2));

        var range = await ctx.SendAsync(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2027, 9, 1), new DateOnly(2028, 3, 1)));
        range.Data!.Select(h => h.OccursOn).Should().Contain([new DateOnly(2027, 9, 15), new DateOnly(2028, 2, 29)]);
        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDaysRangeQuery(new DateOnly(2027, 3, 1), new DateOnly(2027, 1, 1))))
            .Message.Should().Be(CalendarErrorCodes.RangeTooLarge);

        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDayByIdQuery(Guid.NewGuid()))).Data!.Name.Should().Be("Año");
        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDayByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.NonWorkingDayNotFound);
    }

    [Fact]
    public async Task UserConfigurations_ShouldComputeTheEffectiveHours()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var member = Row(("UserId", ctx.OwnerId), ("UserName", " Owner User "), ("HasCalendar", true), ("WorkDays", 62), ("DefaultView", 2), ("WeekStartsOn", 1));
        var own = Row(("UserId", ctx.OtherId), ("UserName", "Other"), ("HasCalendar", true), ("BusinessHoursStart", new TimeOnly(9, 0)), ("BusinessHoursEnd", new TimeOnly(12, 0)));
        ctx.Connection
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(member))
            .Then(CompanyRow(open24: true)).Then(FakeResultSet.FromRows(member))
            .Then(CompanyRow()).Then(NoRows("UserId"))
            .Then(NoRows())
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(own, Row(("UserId", ctx.StrangerId), ("UserName", "Pending"), ("HasCalendar", false))), FakeResultSet.FromScalar(2))
            .Then(NoRows());

        var mine = await ctx.SendAsync(new GetMyCalendarConfigurationQuery());
        mine.Data!.UserName.Should().Be("Owner User");
        mine.Data.EffectiveBusinessHoursStart.Should().Be(new TimeOnly(8, 0));
        mine.Data.EffectiveBusinessHoursEnd.Should().Be(new TimeOnly(17, 0));

        var wholeDay = await ctx.SendAsync(new GetUserCalendarConfigurationQuery(ctx.OwnerId));
        wholeDay.Data!.EffectiveBusinessHoursEnd.Should().BeNull("24 hours");

        (await ctx.SendAsync(new GetUserCalendarConfigurationQuery(ctx.StrangerId))).Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
        (await ctx.SendAsync(new GetUserCalendarConfigurationQuery(ctx.StrangerId))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);

        var page = await ctx.SendAsync(new GetCalendarUserConfigurationsQuery(" oth "));
        page.Data!.Items.First().EffectiveBusinessHoursStart.Should().Be(new TimeOnly(9, 0));
        page.Data.Items.Last().EffectiveBusinessHoursStart.Should().BeNull("no calendar yet");
        ctx.Connection.CapturedParameters["Search"].Should().Be("%oth%");
        (await ctx.SendAsync(new GetCalendarUserConfigurationsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);

        ctx.ActingUserId = null;
        (await ctx.SendAsync(new GetMyCalendarConfigurationQuery())).Message.Should().Be(CalendarErrorCodes.UserRequired);
    }

    [Fact]
    public async Task PeriodsAndCalendars_ShouldBeScopedToTheUser()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection
            .Then(FakeResultSet.FromRows(
                Row(("Id", Guid.NewGuid()), ("UserId", ctx.OwnerId), ("StartDate", new DateOnly(2027, 5, 1)), ("EndDate", new DateOnly(2027, 5, 2)), ("Reason", 1)),
                Row(("Id", Guid.NewGuid()), ("UserId", ctx.OwnerId), ("StartDate", new DateOnly(2027, 2, 1)), ("EndDate", new DateOnly(2027, 2, 2)), ("Reason", 2))))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("UserId", ctx.OtherId), ("UserName", "Other"), ("Reason", 3))), FakeResultSet.FromScalar(1))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("Name", "Mi calendario"), ("OwnerName", " Owner "), ("IsDefault", true))))
            .Then(NoRows());

        var mine = await ctx.SendAsync(new GetMyNonWorkingPeriodsQuery(2027));
        mine.Data!.First().StartDate.Should().Be(new DateOnly(2027, 2, 1));
        ctx.Connection.Commands.Last().Should().Contain("p.UserId = ANY(");

        var page = await ctx.SendAsync(new GetUserNonWorkingPeriodsQuery(ctx.OtherId, 2027));
        page.Data!.Items.Single().Reason.Should().Be(3);
        ctx.Connection.LastCommandText.Should().Contain("p.UserId = @UserId").And.Contain("p.StartDate <= @YearEnd");

        var calendars = await ctx.SendAsync(new GetMyCalendarsQuery());
        calendars.Data!.PendingProvisioning.Should().BeFalse();
        calendars.Data.Calendars.Single().OwnerName.Should().Be("Owner");
        (await ctx.SendAsync(new GetMyCalendarsQuery())).Data!.PendingProvisioning.Should().BeTrue();

        ctx.ActingUserId = null;
        (await ctx.SendAsync(new GetMyNonWorkingPeriodsQuery())).Message.Should().Be(CalendarErrorCodes.UserRequired);
        (await ctx.SendAsync(new GetMyCalendarsQuery())).Message.Should().Be(CalendarErrorCodes.UserRequired);
    }

    [Fact]
    public async Task SettingsQueries_WithoutCompany_ShouldReturnCompanyRequired()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(new GetCalendarActivitiesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarActivityByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarStatusesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarStatusByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarActivityCompaniesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarActivityCompanyByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetUsableCalendarActivityCompaniesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarStatusCompaniesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarStatusCompanyByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetUsableCalendarStatusCompaniesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCurrentCalendarCompanyQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDaysQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDayByIdQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarCompanyNonWorkingDaysRangeQuery(default, default))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetUserCalendarConfigurationQuery(Guid.NewGuid()))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetCalendarUserConfigurationsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetMyNonWorkingPeriodsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetUserNonWorkingPeriodsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new GetMyCalendarsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }
}
