using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using static JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.IntakeTestData;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake.Commands;

/// <summary>
/// SPEC 45 — <c>POST /CalendarChannelIntake/persons</c>: the person, its contacts, the pending confirmation and the
/// <c>PERSON_REVIEW</c> activity in the default user's calendar, in one go; existing identification and foreign
/// contacts create nothing.
/// </summary>
public sealed class RegisterIntakePersonCommandHandlerTests
{
    private static RegisterIntakePersonCommand Command(IntakeTestData data, string channel = CommunicationChannelCodes.WhatsApp,
        string? phone = "+50588887777", string? email = null, string number = "001-020295-0002B") => new(
        PersonType.Physical, " Luis ", " Pérez ", null, data.IdentificationTypeId, number, null, phone, email,
        " Quiere una cita para revisar su contrato ", channel, phone ?? email ?? "x", "wamid.1");

    [Fact]
    public async Task NewPerson_ShouldCreatePersonContactPendingAndReviewActivity()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(NoPerson());
        var before = DateTime.UtcNow;

        var response = await ctx.SendAsync(Command(data));

        response.IsSuccess.Should().BeTrue(response.Message + string.Join(" ", response.Errors ?? []));
        response.Data!.Created.Should().BeTrue();
        response.Data.HasPendingConfirmation.Should().BeTrue();

        var person = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == response.Data.PersonId);
        person.FirstName.Should().Be("Luis");
        person.LastName.Should().Be("Pérez");
        person.GenderId.Should().BeNull("the gender is optional for a person created by a channel");
        person.CompanyId.Should().Be(ctx.CompanyId);

        var contact = await ctx.Db.PersonContacts.IgnoreQueryFilters().SingleAsync(c => c.PersonId == person.Id);
        contact.ContactType.Should().Be(ContactType.WhatsApp);
        contact.ContactValue.Should().Be("+50588887777");
        contact.IsPrimary.Should().BeTrue();

        var pending = await ctx.Db.PersonPendingConfirmations.IgnoreQueryFilters().SingleAsync(p => p.Id == response.Data.PendingConfirmationId);
        pending.Status.Should().Be(PersonPendingConfirmationStatus.Pending);
        pending.Reason.Should().Be("Quiere una cita para revisar su contrato");
        pending.CommunicationChannelId.Should().Be(data.WhatsAppId);
        pending.ChannelIdentifier.Should().Be("+50588887777");
        pending.ReviewEventId.Should().Be(response.Data.ReviewEventId);

        var review = await ctx.ReloadEventAsync(response.Data.ReviewEventId!.Value);
        review.OwnerUserId.Should().Be(ctx.OwnerId, "the review goes to CalendarCompany.DefaultActivityUserId");
        review.CalendarId.Should().Be(ctx.DefaultCalendar[ctx.OwnerId]);
        review.CalendarActivityCompanyId.Should().Be(ctx.Activity[CalendarCodes.Activities.PersonReview]);
        review.CalendarStatusCompanyId.Should().Be(ctx.Status[CalendarCodes.Statuses.Pending]);
        review.Title.Should().Be("Revisar persona creada: Luis Pérez");
        review.Description.Should().Contain("Identificación: 001-020295-0002B").And.Contain("Motivo: Quiere una cita")
            .And.Contain("Canal: WHATSAPP").And.Contain("Teléfono: +50588887777");
        review.RequesterPersonId.Should().Be(person.Id);
        review.OriginChannelId.Should().Be(data.WhatsAppId);
        review.OriginType.Should().Be(CalendarEventOriginType.Phone);
        review.OriginIdentifier.Should().Be("+50588887777");
        review.OriginReference.Should().BeNull("the message id stays free for the activity the agent creates next");
        review.OriginUserId.Should().Be(data.AgentId);
        review.StartUtc.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(before.AddMinutes(16));
        (review.StartUtc.Minute % 15).Should().Be(0);
        review.EndUtc.Should().Be(review.StartUtc.AddMinutes(15));

        var log = (await ctx.LogsAsync(review.Id)).Single();
        log.Action.Should().Be(CalendarEventLogAction.Created);
        log.ChannelId.Should().Be(data.WhatsAppId);
        log.ActorUserId.Should().Be(data.AgentId);
        log.ActorIdentifier.Should().Be("+50588887777");

        ctx.Connection.Commands[0].Should().Contain("p.identificationnumber = @IdentificationNumber");
        ctx.Connection.Commands[1].Should().Contain("regexp_replace");
    }

    [Fact]
    public async Task EmailChannel_ShouldStoreBothContactsAndEmailOrigin()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(NoPerson()).Then(NoPerson());

        var response = await ctx.SendAsync(Command(data, CommunicationChannelCodes.SendGrid, "+50588881111", "luis@correo.com"));

        response.IsSuccess.Should().BeTrue(response.Message);
        var contacts = await ctx.Db.PersonContacts.IgnoreQueryFilters().Where(c => c.PersonId == response.Data!.PersonId).ToListAsync();
        contacts.Select(c => c.ContactType).Should().BeEquivalentTo([ContactType.MobilePhone, ContactType.PrimaryEmail]);
        contacts.Should().OnlyContain(c => c.IsPrimary);
        (await ctx.ReloadEventAsync(response.Data!.ReviewEventId!.Value)).OriginType.Should().Be(CalendarEventOriginType.Email);
    }

    [Fact]
    public async Task ExistingIdentification_ShouldReturnThePersonWithoutCreatingAnything()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(PersonRow(data.PersonId, hasPending: true));

        var response = await ctx.SendAsync(Command(data, number: "001-010190-0001A"));

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEquivalentTo(new { PersonId = data.PersonId, Created = false, HasPendingConfirmation = true, PendingConfirmationId = (Guid?)null });
        (await ctx.Db.Persons.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await ctx.Db.PersonContacts.IgnoreQueryFilters().AnyAsync()).Should().BeFalse("the new phone is never added to an existing person");
        (await ctx.Db.CalendarEvents.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task PhoneOfAnotherPerson_ShouldFailWithoutRevealingIt()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(PersonRow(data.PersonId));

        var response = await ctx.SendAsync(Command(data));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(CalendarErrorCodes.IntakeContactBelongsToOtherPerson);
        string.Join(" ", response.Errors!).Should().NotContain("Ana");
        (await ctx.Db.Persons.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task EmailOfSeveralPersons_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(PersonRow(data.PersonId, more: Guid.NewGuid()));

        var response = await ctx.SendAsync(Command(data, CommunicationChannelCodes.SendGrid, null, "ana@correo.com"));

        response.Message.Should().Be(CalendarErrorCodes.IntakeContactBelongsToOtherPerson);
    }

    [Fact]
    public async Task PersonReviewDisabled_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        var review = await ctx.Db.CalendarActivityCompanies.SingleAsync(a => a.Id == ctx.Activity[CalendarCodes.Activities.PersonReview]);
        review.IsActive = false;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        ctx.Connection.Then(NoPerson()).Then(NoPerson());

        var response = await ctx.SendAsync(Command(data));

        response.Message.Should().Be(CalendarErrorCodes.ActivityNotEnabled);
        (await ctx.Db.Persons.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DefaultUserWithoutCalendar_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        var company = await ctx.Db.CalendarCompanies.SingleAsync();
        company.DefaultActivityUserId = ctx.StrangerId;
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        ctx.Connection.Then(NoPerson()).Then(NoPerson());

        var response = await ctx.SendAsync(Command(data));

        response.Message.Should().Be(CalendarErrorCodes.IntakeTargetUserHasNoCalendar);
    }

    [Fact]
    public async Task InvalidChannelOrCompany_ShouldFailBeforeAnyLookup()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);

        (await ctx.SendAsync(Command(data, CommunicationChannelCodes.Web))).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel);
        (await ctx.SendAsync(Command(data, CommunicationChannelCodes.Twilio))).Message.Should().Be(CalendarErrorCodes.IntakeInvalidChannel, "the channel is not registered");

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(Command(data))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
        ctx.Connection.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task CompanyWithoutCalendar_ShouldFail()
    {
        using var ctx = await CalendarTestContext.CreateAsync(configureCompany: false);
        var data = await SeedAsync(ctx);

        (await ctx.SendAsync(Command(data))).Message.Should().Be(CalendarErrorCodes.CompanyNotConfigured);
    }

    [Fact]
    public async Task LegalPerson_ShouldUseTheCommercialNameInTheReview()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var data = await SeedAsync(ctx);
        ctx.Connection.Then(NoPerson()).Then(NoPerson());

        var response = await ctx.SendAsync(Command(data) with { PersonType = PersonType.Legal, LastName = null, CommercialName = "Acme S.A." });

        (await ctx.ReloadEventAsync(response.Data!.ReviewEventId!.Value)).Title.Should().Be("Revisar persona creada: Acme S.A.");
    }

    [Theory]
    [InlineData("2027-03-01T10:00:00", "2027-03-01T10:00:00")]
    [InlineData("2027-03-01T10:00:01", "2027-03-01T10:15:00")]
    [InlineData("2027-03-01T10:52:00", "2027-03-01T11:00:00")]
    public void RoundUp_ShouldMoveToTheNextQuarter(string now, string expected)
    {
        var utc = DateTime.SpecifyKind(DateTime.Parse(now, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

        RegisterIntakePersonCommandHandler.RoundUp(utc).Should().Be(DateTime.SpecifyKind(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
    }
}
