using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeAvailability;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeCompanies;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakeIdentificationTypes;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.GetIntakePersonEvents;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakePerson;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakeUserByEmail;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using static JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.IntakeTestData;
using static JOIN.Application.UnitTest.UseCases.Calendars.Queries.CalendarQueryHandlersTests;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Queries;

/// <summary>
/// SPEC 45 — Dapper reads of the agent over a fake connection: requester lookup (phone digits, last 8 digits,
/// e-mail without case, ambiguity), user by exact e-mail, companies of the agent, identification types,
/// availability of the default user and the person's activities.
/// </summary>
public sealed class IntakeQueryHandlersTests
{
    [Fact]
    public async Task LookupPerson_ByPhone_ShouldCompareDigitsAndTheLastEight()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var personId = Guid.NewGuid();
        ctx.Connection.Then(PersonRow(personId, " Ana López ", hasPending: true));

        var response = await ctx.SendAsync(new LookupIntakePersonQuery("whatsapp", "+505 8888-7777", null, null));

        response.Data.Should().BeEquivalentTo(new { Found = true, PersonId = (Guid?)personId, FullName = "Ana López", HasPendingConfirmation = true, Ambiguous = false });
        ctx.Connection.LastCommandText.Should().Contain("regexp_replace(pc.contactvalue, '[^0-9]', '', 'g') = @Digits")
            .And.Contain("RIGHT(").And.Contain("pc.contacttype IN (3, 4, 5)").And.Contain("p.isactive = TRUE").And.Contain("pc.isactive = TRUE");
        ctx.Connection.CapturedParameters["Digits"].Should().Be("50588887777");
        ctx.Connection.CapturedParameters["LocalDigits"].Should().Be("88887777");
        ctx.Connection.CapturedParameters["TenantId"].Should().Be(ctx.CompanyId);
    }

    [Fact]
    public async Task LookupPerson_ShouldReportAmbiguityAndMisses()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection.Then(PersonRow(Guid.NewGuid(), more: Guid.NewGuid())).Then(NoPerson());

        var ambiguous = await ctx.SendAsync(new LookupIntakePersonQuery("WHATSAPP", "+50588887777", null, null));
        var missing = await ctx.SendAsync(new LookupIntakePersonQuery("SENDGRID", " Ana@Correo.com ", null, null));
        var tooShort = await ctx.SendAsync(new LookupIntakePersonQuery("WHATSAPP", "+505 777", null, null));

        ambiguous.Data!.Ambiguous.Should().BeTrue();
        ambiguous.Data.Found.Should().BeFalse();
        ambiguous.Data.PersonId.Should().BeNull("no data of anybody travels back on an ambiguous match");
        missing.Data!.Found.Should().BeFalse();
        ctx.Connection.LastCommandText.Should().Contain("LOWER(TRIM(pc.contactvalue)) = LOWER(TRIM(@Email))").And.Contain("pc.contacttype IN (1, 2)");
        ctx.Connection.CapturedParameters["Email"].Should().Be("Ana@Correo.com");
        tooShort.Data!.Found.Should().BeFalse();
        ctx.Connection.Commands.Should().HaveCount(2, "a number shorter than 8 digits is not looked up");
    }

    [Fact]
    public async Task LookupPerson_ByIdentification_AndErrors()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var personId = Guid.NewGuid();
        var typeId = Guid.NewGuid();
        ctx.Connection.Then(PersonRow(personId));

        (await ctx.SendAsync(new LookupIntakePersonQuery(null, null, typeId, " 001-1 "))).Data!.PersonId.Should().Be(personId);
        ctx.Connection.CapturedParameters["IdentificationNumber"].Should().Be("001-1");
        ctx.Connection.CapturedParameters["IdentificationTypeId"].Should().Be(typeId);

        (await ctx.SendAsync(new LookupIntakePersonQuery(null, "+505", Guid.Empty, "1"))).Message.Should().Be(CalendarErrorCodes.IntakeLookupCriteriaRequired);
        (await ctx.SendAsync(new LookupIntakePersonQuery("WEB", "x", null, null))).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new LookupIntakePersonQuery("WHATSAPP", "+50588887777", null, null))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Resolver_ShouldNotQueryWithoutCriteria()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var resolver = ctx.Get<IntakeRequesterResolver>();

        (await resolver.LookupByEmailAsync(ctx.CompanyId, " ", CancellationToken.None)).Should().Be(IntakePersonMatch.None);
        (await resolver.LookupByIdentificationAsync(ctx.CompanyId, Guid.Empty, "1", CancellationToken.None)).Should().Be(IntakePersonMatch.None);
        (await resolver.LookupByChannelAsync(ctx.CompanyId, "WEB", "x", CancellationToken.None)).Should().Be(IntakePersonMatch.None);
        IntakeRequesterResolver.Digits(null).Should().BeEmpty();
        IntakePersonMatch.AmbiguousMatch.Exists.Should().BeTrue();
        ctx.Connection.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task LookupUser_ShouldMatchTheExactEmailOnly()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        ctx.Connection
            .Then(FakeResultSet.FromRows(Row(("UserId", ctx.OtherId), ("FullName", " Other User "))))
            .Then(NoRows("UserId", "FullName"));

        var found = await ctx.SendAsync(new LookupIntakeUserByEmailQuery(" Other@T.local "));
        found.Data.Should().BeEquivalentTo(new { Found = true, UserId = (Guid?)ctx.OtherId, FullName = "Other User" });
        ctx.Connection.LastCommandText.Should().Contain("LOWER(TRIM(u.Email)) = LOWER(TRIM(@Email))").And.Contain("Calendar.UserConfigurations");

        (await ctx.SendAsync(new LookupIntakeUserByEmailQuery("x@t.local"))).Data!.Found.Should().BeFalse();
        (await ctx.SendAsync(new LookupIntakeUserByEmailQuery(" "))).Data!.Found.Should().BeFalse();
        ctx.Connection.Commands.Should().HaveCount(2);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new LookupIntakeUserByEmailQuery("x@t.local"))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Companies_ShouldRequireTheAgentRole()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var companyId = Guid.NewGuid();
        ctx.Connection.Then(FakeResultSet.FromRows(Row(("CompanyId", companyId), ("Name", "JOIN"), ("TaxId", "J-1"))));

        (await ctx.SendAsync(new GetIntakeCompaniesQuery("x"))).Message.Should().Be(CalendarErrorCodes.IntakeCompanyNotAllowed);

        ctx.CurrentUser.Setup(x => x.IsInRole("Agent")).Returns(true);
        var response = await ctx.SendAsync(new GetIntakeCompaniesQuery(" JO "));

        response.Data!.Single().Should().Be(new JOIN.Application.DTO.Calendars.IntakeCompanyDto(companyId, "JOIN", "J-1"));
        ctx.Connection.CapturedParameters["Search"].Should().Be("%jo%");
        ctx.Connection.CapturedParameters["ModuleName"].Should().Be("Calendar");
        ctx.Connection.CapturedParameters["UserId"].Should().Be(ctx.OwnerId);
        ctx.Connection.LastCommandText.Should().Contain("Security.UserCompanies").And.Contain("Calendar.CalendarCompanies").And.Contain("cm.IsActive = TRUE");

        ctx.ActingUserId = null;
        (await ctx.SendAsync(new GetIntakeCompaniesQuery(null))).Message.Should().Be(CalendarErrorCodes.UserRequired);
    }

    [Fact]
    public async Task IdentificationTypes_ShouldListActiveRows()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        ctx.Connection.Then(FakeResultSet.FromRows(Row(("Id", id), ("Name", "Cédula"))));

        (await ctx.SendAsync(new GetIntakeIdentificationTypesQuery())).Data!.Single().Name.Should().Be("Cédula");
        ctx.Connection.LastCommandText.Should().Contain("isactive = TRUE");
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new GetIntakeIdentificationTypesQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Availability_ShouldUseTheDefaultUserAndReportErrors()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var user = Row(("ConfigurationId", Guid.NewGuid()), ("WorkDays", 62));
        var meeting = Row(("ActivityCompanyId", Guid.NewGuid()), ("DefaultDurationMinutes", 60), ("AllowsOverlap", false), ("RequiresBusinessHours", true));
        ctx.Connection
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(Row(("DefaultActivityUserId", ctx.OwnerId)))).Then(FakeResultSet.FromRows(user)).Then(FakeResultSet.FromRows(meeting))
            .Then(NoRows("Id")).Then(NoRows("Id", "UserId")).Then(NoRows("Id", "Date"))
            .Then(NoRows("Id", "TimeZone"))
            .Then(CompanyRow()).Then(NoRows("ConfigurationId"))
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(Row(("ConfigurationId", null), ("WorkDays", 0))))
            .Then(CompanyRow()).Then(FakeResultSet.FromRows(user)).Then(NoRows("DefaultDurationMinutes"))
            .Then(CompanyRow()).Then(NoRows("DefaultActivityUserId"));
        var query = new GetIntakeAvailabilityQuery(null, Local(2027, 3, 1, 0), Local(2027, 3, 2, 0), "meeting");

        var response = await ctx.SendAsync(query);

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.TimeZone.Should().Be("America/Managua");
        response.Data.Slots.Should().HaveCount(9, "08:00–17:00 in one-hour blocks");

        (await ctx.SendAsync(query with { To = Local(2027, 3, 20, 0) })).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        (await ctx.SendAsync(query with { UserId = ctx.StrangerId })).Message.Should().Be(CalendarErrorCodes.UserNotInCompany);
        (await ctx.SendAsync(query with { UserId = ctx.OtherId })).Message.Should().Be(CalendarErrorCodes.IntakeTargetUserHasNoCalendar);
        (await ctx.SendAsync(query with { UserId = ctx.OtherId })).Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task PersonEvents_ShouldListTheRequesterActivitiesWithoutCancelled()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var personId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        Dictionary<string, object?> EventRow(Guid id, int day, string status) => Row(
            ("Id", id), ("SeriesMasterId", null), ("OriginalStartUtc", null), ("StartUtc", Utc(2027, 3, day, 10)), ("EndUtc", Utc(2027, 3, day, 11)),
            ("RecurrenceRule", null), ("RecurrenceEndUtc", null), ("IsAllDay", false), ("Title", "Cita"),
            ("StatusCompanyId", statusId), ("StatusCode", status), ("StatusName", "Pendiente"), ("StatusColor", "#FFB300"), ("OwnerName", " Owner User "));
        var kept = Guid.NewGuid();
        ctx.Connection
            .Then(FakeResultSet.FromRows(Row(("TimeZone", "America/Managua"))))
            .Then(FakeResultSet.FromRows(EventRow(Guid.NewGuid(), 3, CalendarCodes.Statuses.Cancelled), EventRow(kept, 2, CalendarCodes.Statuses.Pending)))
            .Then(NoRows("TimeZone"));
        var query = new GetIntakePersonEventsQuery(personId, Local(2027, 3, 1, 0), Local(2027, 3, 10, 0));

        var response = await ctx.SendAsync(query);

        var only = response.Data!.Single();
        only.EventId.Should().Be(kept);
        only.Status.Should().Be(new JOIN.Application.DTO.Calendars.IntakeStatusDto(statusId, CalendarCodes.Statuses.Pending, "Pendiente", "#FFB300"));
        only.OwnerFullName.Should().Be("Owner User");
        ctx.Connection.CapturedParameters["RequesterPersonId"].Should().Be(personId);
        ctx.Connection.CapturedParameters["PersonReview"].Should().Be(CalendarCodes.Activities.PersonReview, "the internal review activity is never listed");

        (await ctx.SendAsync(query with { To = Local(2027, 7, 1, 0) })).Message.Should().Be(CalendarErrorCodes.RangeTooLarge);
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(query)).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task PersonEvents_ShouldExpandSeriesWithTheirExceptions()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var master = Guid.NewGuid();
        var exception = Guid.NewGuid();
        Dictionary<string, object?> SlotRow(Guid id, Guid? seriesMaster, DateTime? originalStart, DateTime start, string? rule, string status) => Row(
            ("Id", id), ("SeriesMasterId", seriesMaster), ("OriginalStartUtc", originalStart), ("StartUtc", start), ("EndUtc", start.AddHours(1)),
            ("RecurrenceRule", rule), ("RecurrenceEndUtc", null), ("IsAllDay", false), ("Title", "Terapia"),
            ("StatusCompanyId", Guid.NewGuid()), ("StatusCode", status), ("StatusName", status), ("StatusColor", "#000000"), ("OwnerName", "Owner User"));
        ctx.Connection
            .Then(FakeResultSet.FromRows(Row(("TimeZone", "America/Managua"))))
            .Then(FakeResultSet.FromRows(SlotRow(master, null, null, Utc(2027, 3, 1, 10), "FREQ=WEEKLY;COUNT=3", CalendarCodes.Statuses.Pending)))
            .Then(FakeResultSet.FromRows(SlotRow(exception, master, Utc(2027, 3, 8, 10), Utc(2027, 3, 8, 10), null, CalendarCodes.Statuses.Cancelled)));

        var response = await ctx.SendAsync(new GetIntakePersonEventsQuery(Guid.NewGuid(), Local(2027, 3, 1, 0), Local(2027, 3, 31, 0)));

        response.Data!.Select(e => e.OccurrenceStartUtc).Should().Equal(Utc(2027, 3, 1, 10), Utc(2027, 3, 15, 10));
        response.Data.Should().OnlyContain(e => e.EventId == master, "occurrences are addressed through the series master");
        ctx.Connection.LastCommandText.Should().Contain("e.SeriesMasterId = ANY(");
    }

    [Fact]
    public void Channels_ShouldClassifyByCode()
    {
        IntakeChannels.IsPhone(" whatsapp ").Should().BeTrue();
        IntakeChannels.IsPhone("TWILIO").Should().BeTrue();
        IntakeChannels.IsEmail("SendGrid").Should().BeTrue();
        IntakeChannels.IsSupported("WEB").Should().BeFalse();
        IntakeChannels.IsSupported(null).Should().BeFalse();
        IntakeChannels.OriginTypeOf("SENDGRID").Should().Be(CalendarEventOriginType.Email);
        IntakeChannels.OriginTypeOf("TELEGRAM").Should().Be(CalendarEventOriginType.Phone);
    }
}
