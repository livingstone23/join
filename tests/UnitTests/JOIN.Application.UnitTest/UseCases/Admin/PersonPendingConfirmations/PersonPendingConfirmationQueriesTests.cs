using FluentAssertions;
using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using static JOIN.Application.UnitTest.UseCases.Calendars.Queries.CalendarQueryHandlersTests;

namespace JOIN.Application.UnitTest.UseCases.Admin.PersonPendingConfirmations;

/// <summary>
/// SPEC 45 — Dapper reads of the pending-persons inbox: tenant, soft delete, default status, filters, paging, the
/// review activity status, and the detail with contacts and activities.
/// </summary>
public sealed class PersonPendingConfirmationQueriesTests
{
    private static Dictionary<string, object?> PendingRow(Guid id, Guid personId) => Row(
        ("Id", id), ("PersonId", personId), ("PersonFullName", "Luis Pérez"), ("Identification", "002"),
        ("ChannelId", Guid.NewGuid()), ("ChannelName", "WhatsApp"), ("ChannelIdentifier", "+50588886666"), ("Reason", "Cita"),
        ("RequestedUtc", new DateTime(2027, 3, 1, 16, 0, 0)), ("Status", 1), ("ReviewEventId", Guid.NewGuid()), ("ReviewEventStatus", "PENDING"),
        ("CustomerId", null), ("ResolvedByUserId", null), ("ResolvedUtc", null), ("ResolutionNotes", null));

    [Fact]
    public async Task Inbox_ShouldFilterPageAndDefaultToPending()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        ctx.Connection
            .Then(FakeResultSet.FromRows(PendingRow(id, Guid.NewGuid())), FakeResultSet.FromScalar(21))
            .Then(FakeResultSet.Empty("Id"), FakeResultSet.FromScalar(0));

        var response = await ctx.SendAsync(new GetPersonPendingConfirmationsQuery(null, channelId, new DateTime(2027, 3, 1), new DateTime(2027, 3, 31), " luis ", 2, 10));

        response.IsSuccess.Should().BeTrue(response.Message);
        var item = response.Data!.Items.Single();
        item.Id.Should().Be(id);
        item.ReviewEventStatus.Should().Be("PENDING");
        item.RequestedUtc.Kind.Should().Be(DateTimeKind.Utc);
        response.Data.TotalCount.Should().Be(21);
        ctx.Connection.CapturedParameters["Status"].Should().Be(1);
        ctx.Connection.CapturedParameters["Search"].Should().Be("%luis%");
        ctx.Connection.CapturedParameters["ChannelId"].Should().Be(channelId);
        ctx.Connection.CapturedParameters["Offset"].Should().Be(10);
        ctx.Connection.CapturedParameters["TenantId"].Should().Be(ctx.CompanyId);
        ctx.Connection.LastCommandText.Should().Contain("ppc.gcrecord = 0").And.Contain("ppc.companyid = @TenantId")
            .And.Contain("calendar.events e").And.Contain("ORDER BY ppc.requestedutc DESC").And.Contain("ILIKE @Search");

        (await ctx.SendAsync(new GetPersonPendingConfirmationsQuery(Status: 4))).Data!.Items.Should().BeEmpty();
        ctx.Connection.CapturedParameters["Status"].Should().Be(4);
        (await ctx.SendAsync(new GetPersonPendingConfirmationsQuery(Status: 9))).Message.Should().Be(CalendarErrorCodes.PersonPendingInvalidStatus);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new GetPersonPendingConfirmationsQuery())).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Detail_ShouldIncludeContactsAndActivities()
    {
        using var ctx = await CalendarTestContext.CreateAsync();
        var id = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        ctx.Connection
            .Then(FakeResultSet.FromRows(PendingRow(id, personId)))
            .Then(FakeResultSet.FromRows(Row(("Id", Guid.NewGuid()), ("ContactType", 5), ("ContactValue", "+50588886666"), ("IsPrimary", true),
                ("Comments", null), ("Created", new DateTime(2027, 3, 1, 16, 5, 0)))))
            .Then(FakeResultSet.FromRows(Row(("EventId", eventId), ("StartUtc", new DateTime(2027, 3, 1, 16, 15, 0)), ("EndUtc", new DateTime(2027, 3, 1, 16, 30, 0)),
                ("Title", "Revisar persona creada: Luis Pérez"), ("StatusId", Guid.NewGuid()), ("StatusCode", "PENDING"), ("StatusName", "Pendiente"),
                ("StatusColor", "#FFB300"), ("OwnerFullName", "Owner User"))))
            .Then(FakeResultSet.Empty("Id"));

        var response = await ctx.SendAsync(new GetPersonPendingConfirmationByIdQuery(id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Header.PersonId.Should().Be(personId);
        var contact = response.Data.Contacts.Single();
        contact.ContactType.Should().Be("5");
        contact.ContactName.Should().Be("WhatsApp");
        contact.CreatedAt.Should().Be("2027-03-01 16:05");
        var activity = response.Data.Events.Single();
        activity.EventId.Should().Be(eventId);
        activity.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        activity.Status.Code.Should().Be("PENDING");
        ctx.Connection.CapturedParameters["Cancelled"].Should().Be(CalendarCodes.Statuses.Cancelled);
        ctx.Connection.CapturedParameters["PersonId"].Should().Be(personId);

        (await ctx.SendAsync(new GetPersonPendingConfirmationByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.PersonPendingNotFound);
        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new GetPersonPendingConfirmationByIdQuery(id))).Message.Should().Be(CalendarErrorCodes.CompanyRequired);
    }
}
