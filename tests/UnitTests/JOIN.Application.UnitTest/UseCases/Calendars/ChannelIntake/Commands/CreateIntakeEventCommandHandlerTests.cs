using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using static JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles.CalendarTestContext;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Commands;

/// <summary>
/// SPEC 45 — <c>POST /CalendarChannelIntake/events</c>: recipient by exact e-mail or the default user, initial status,
/// R4 + R5 (the agent is never the owner), full origin, and deduplication by message id (sequential and race).
/// Dates: March 2027, America/Managua (UTC-6); 2027-03-01 is a Monday.
/// </summary>
public sealed class CreateIntakeEventCommandHandlerTests
{
    internal static FakeResultSet NoDuplicate() => FakeResultSet.Empty("Id", "OwnerUserId", "StartUtc", "EndUtc", "StatusId", "StatusCode", "StatusName", "StatusColor", "OwnerFullName");

    internal static FakeResultSet Duplicate(Guid eventId, Guid ownerId) => FakeResultSet.FromRows(new Dictionary<string, object?>
    {
        ["Id"] = eventId,
        ["OwnerUserId"] = ownerId,
        ["StartUtc"] = Utc(2027, 3, 1, 10),
        ["EndUtc"] = Utc(2027, 3, 1, 11),
        ["StatusId"] = Guid.NewGuid(),
        ["StatusCode"] = CalendarCodes.Statuses.Pending,
        ["StatusName"] = "Pendiente",
        ["StatusColor"] = "#FFB300",
        ["OwnerFullName"] = "Owner User"
    });

    internal static CreateIntakeEventCommand Command(Guid requesterPersonId, string? targetEmail = null, int hour = 10,
        string activity = CalendarCodes.Activities.Meeting, string reference = "wamid.A") => new(
        activity, " Cita: revisión de contrato ", "Ana quiere revisar la cláusula 4", null, null, null, false,
        Local(2027, 3, 1, hour), null, null, null, null, targetEmail, requesterPersonId,
        "whatsapp", null, "+50588887777", reference);

    [Fact]
    public async Task WithoutTargetEmail_ShouldGoToTheDefaultUserWithFullOrigin()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());

        var response = await ctx.SendAsync(Command(data.PersonId));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        var dto = response.Data!;
        dto.IsDuplicate.Should().BeFalse();
        dto.RoutedToDefaultUser.Should().BeTrue();
        dto.OwnerFullName.Should().Be("Owner User");
        dto.Status.Code.Should().Be(CalendarCodes.Statuses.Pending);
        dto.StartUtc.Should().Be(Utc(2027, 3, 1, 10));
        dto.EndUtc.Should().Be(Utc(2027, 3, 1, 11), "without end the type's duration applies");
        dto.TimeZone.Should().Be("America/Managua");

        var stored = await ctx.ReloadEventAsync(dto.EventId);
        stored.OwnerUserId.Should().Be(ctx.OwnerId);
        stored.CalendarId.Should().Be(ctx.DefaultCalendar[ctx.OwnerId]);
        stored.Title.Should().Be("Cita: revisión de contrato");
        stored.Summary.Should().Be("Ana quiere revisar la cláusula 4");
        stored.RequesterPersonId.Should().Be(data.PersonId);
        stored.OriginChannelId.Should().Be(data.WhatsAppId);
        stored.OriginType.Should().Be(CalendarEventOriginType.Phone);
        stored.OriginIdentifier.Should().Be("+50588887777");
        stored.OriginReference.Should().Be("wamid.A");
        stored.OriginUserId.Should().Be(data.AgentId);

        var log = (await ctx.LogsAsync(dto.EventId)).Single();
        log.ChannelId.Should().Be(data.WhatsAppId);
        log.ActorUserId.Should().Be(data.AgentId);
        log.ActorIdentifier.Should().Be("+50588887777");
        ctx.Connection.LastCommandText.Should().Contain("e.originreference = @Reference");
        ctx.Connection.CapturedParameters["Reference"].Should().Be("wamid.A");
    }

    [Fact]
    public async Task MatchingEmail_ShouldGoToThatUserIgnoringCase()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());

        var response = await ctx.SendAsync(Command(data.PersonId, " OTHER@T.LOCAL "));

        response.Data!.RoutedToDefaultUser.Should().BeFalse();
        response.Data.OwnerFullName.Should().Be("Other User");
        var stored = await ctx.ReloadEventAsync(response.Data.EventId);
        stored.OwnerUserId.Should().Be(ctx.OtherId);
        stored.CalendarId.Should().Be(ctx.DefaultCalendar[ctx.OtherId]);
        stored.Description.Should().BeNull();
    }

    [Theory]
    [InlineData("nadie@t.local")]
    [InlineData("stranger@t.local")]
    public async Task UnmatchedEmail_ShouldGoToTheDefaultUserWithANote(string email)
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());

        var response = await ctx.SendAsync(Command(data.PersonId, email) with { Description = "Trae su contrato" });

        response.Data!.RoutedToDefaultUser.Should().BeTrue();
        var stored = await ctx.ReloadEventAsync(response.Data.EventId);
        stored.OwnerUserId.Should().Be(ctx.OwnerId);
        stored.Description.Should().Be($"Trae su contrato\n\nEl solicitante indicó ser atendido por: {email} (no corresponde a un usuario de la empresa).");
    }

    [Fact]
    public async Task ExplicitOriginType_ShouldBeKept()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());

        var response = await ctx.SendAsync(Command(data.PersonId) with { OriginType = "email" });

        (await ctx.ReloadEventAsync(response.Data!.EventId)).OriginType.Should().Be(CalendarEventOriginType.Email);
    }

    [Fact]
    public async Task SameReference_ShouldReturnTheExistingActivity()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        var existing = Guid.NewGuid();
        ctx.Connection.Then(Duplicate(existing, ctx.OwnerId));

        var response = await ctx.SendAsync(Command(data.PersonId));

        response.IsSuccess.Should().BeTrue();
        response.Data!.EventId.Should().Be(existing);
        response.Data.IsDuplicate.Should().BeTrue();
        response.Data.RoutedToDefaultUser.Should().BeTrue();
        response.Data.Status.Code.Should().Be(CalendarCodes.Statuses.Pending);
        (await ctx.Db.CalendarEvents.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LostRace_ShouldAnswerWithTheWinner()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        var winner = Guid.NewGuid();
        var violation = new InvalidOperationException("23505");
        ctx.UnitOfWork.ScheduleLockFailure = violation;
        ctx.ErrorClassifier.Setup(x => x.IsUniqueViolation(violation, CreateIntakeEventCommandHandler.OriginReferenceIndex)).Returns(true);
        ctx.Connection.Then(NoDuplicate()).Then(Duplicate(winner, ctx.OtherId));

        var response = await ctx.SendAsync(Command(data.PersonId));

        response.IsSuccess.Should().BeTrue();
        response.Data!.EventId.Should().Be(winner);
        response.Data.IsDuplicate.Should().BeTrue();
        response.Data.RoutedToDefaultUser.Should().BeFalse();
    }

    [Fact]
    public async Task UniqueViolationWithoutWinnerOrOtherErrors_ShouldPropagate()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        var violation = new InvalidOperationException("23505");
        ctx.ErrorClassifier.Setup(x => x.IsUniqueViolation(violation, It.IsAny<string>())).Returns(true);

        ctx.UnitOfWork.ScheduleLockFailure = violation;
        ctx.Connection.Then(NoDuplicate()).Then(NoDuplicate());
        await FluentActions.Awaiting(() => ctx.SendAsync(Command(data.PersonId))).Should().ThrowAsync<InvalidOperationException>();

        ctx.UnitOfWork.ScheduleLockFailure = new TimeoutException();
        ctx.Connection.Then(NoDuplicate());
        await FluentActions.Awaiting(() => ctx.SendAsync(Command(data.PersonId, reference: "wamid.B"))).Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Overlap_ShouldFailSoTheAgentProposesAnotherTime()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate()).Then(NoDuplicate());
        (await ctx.SendAsync(Command(data.PersonId))).IsSuccess.Should().BeTrue();

        var response = await ctx.SendAsync(Command(data.PersonId, reference: "wamid.B"));

        response.Message.Should().Be(CalendarErrorCodes.EventOverlap);
    }

    [Fact]
    public async Task OutsideBusinessHours_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        ctx.Connection.Then(NoDuplicate());

        var response = await ctx.SendAsync(Command(data.PersonId, hour: 19));

        response.Message.Should().Be(CalendarErrorCodes.OutsideBusinessHours);
    }

    [Fact]
    public async Task InvalidRequesterOrType_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        var inactive = new Person { CompanyId = ctx.CompanyId, FirstName = "Inactiva", IdentificationNumber = "X" };
        inactive.Deactivate();
        var foreign = new Person { CompanyId = Guid.NewGuid(), FirstName = "Ajena", IdentificationNumber = "Y" };
        ctx.Db.Persons.AddRange(inactive, foreign);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        ctx.Connection.Then(NoDuplicate()).Then(NoDuplicate()).Then(NoDuplicate());

        (await ctx.SendAsync(Command(inactive.Id))).Message.Should().Be(CalendarErrorCodes.InvalidLink);
        (await ctx.SendAsync(Command(foreign.Id))).Message.Should().Be(CalendarErrorCodes.InvalidLink);
        (await ctx.SendAsync(Command(data.PersonId, activity: "UNKNOWN"))).Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
    }

    [Fact]
    public async Task DefaultUserWithoutCalendar_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);
        var company = await ctx.Db.CalendarCompanies.SingleAsync();
        company.DefaultActivityUserId = ctx.StrangerId;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        ctx.Connection.Then(NoDuplicate());

        (await ctx.SendAsync(Command(data.PersonId))).Message.Should().Be(CalendarErrorCodes.IntakeTargetUserHasNoCalendar);
    }

    [Fact]
    public async Task ContextErrors_ShouldFailBeforeAnyRead()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await IntakeTestData.SeedAsync(ctx);

        (await ctx.SendAsync(Command(data.PersonId) with { OriginChannelCode = CommunicationChannelCodes.Web })).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel);
        ctx.ActingCompanyId = Guid.NewGuid();
        (await ctx.SendAsync(Command(data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Command(data.PersonId))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        ctx.Connection.Commands.Should().BeEmpty();
    }
}
