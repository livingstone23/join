using JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Application.UnitTest.UseCases.Calendars.ChannelIntake;

/// <summary>
/// SPEC 45 test data over <see cref="CalendarTestContext"/>: the WHATSAPP and SENDGRID channels, the service user of
/// the agent (member of the company, acting user) and an active person who already wrote before. The Dapper reads of
/// the requester resolver are answered with <see cref="NoPerson"/> / <see cref="PersonRow"/>.
/// </summary>
internal sealed class IntakeTestData
{
    public Guid AgentId { get; } = Guid.NewGuid();
    public Guid WhatsAppId { get; private set; }
    public Guid SendGridId { get; private set; }
    public Guid IdentificationTypeId { get; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }

    public static async Task<IntakeTestData> SeedAsync(CalendarTestContext ctx)
    {
        var data = new IntakeTestData();
        var whatsApp = new CommunicationChannel { Name = "WhatsApp", Code = CommunicationChannelCodes.WhatsApp, Provider = "Meta", IsActive = true };
        var sendGrid = new CommunicationChannel { Name = "SendGrid", Code = CommunicationChannelCodes.SendGrid, Provider = "Twilio", IsActive = true };
        data.WhatsAppId = whatsApp.Id;
        data.SendGridId = sendGrid.Id;
        ctx.Db.CommunicationChannels.AddRange(whatsApp, sendGrid);

        ctx.Db.ApplicationUsers.Add(new ApplicationUser { Id = data.AgentId, FirstName = "Agent", LastName = "Bot", Email = "agent@t.local", UserName = "agent@t.local", IsActive = true });
        ctx.Db.UserCompanies.Add(new UserCompany { UserId = data.AgentId, CompanyId = ctx.CompanyId, IsDefault = true });

        var person = new Person
        {
            CompanyId = ctx.CompanyId,
            PersonType = PersonType.Physical,
            FirstName = "Ana",
            LastName = "López",
            IdentificationTypeId = data.IdentificationTypeId,
            IdentificationNumber = "001-010190-0001A"
        };
        data.PersonId = person.Id;
        ctx.Db.Persons.Add(person);

        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();
        ctx.ActingUserId = data.AgentId;
        return data;
    }

    public static FakeResultSet NoPerson() => FakeResultSet.Empty("Id", "FullName", "HasPending");

    public static FakeResultSet PersonRow(Guid id, string name = "Ana López", bool hasPending = false, params Guid[] more) =>
        FakeResultSet.FromRows(
            new[] { id }.Concat(more)
                .Select(personId => (IDictionary<string, object?>)new Dictionary<string, object?>
                {
                    ["Id"] = personId,
                    ["FullName"] = name,
                    ["HasPending"] = hasPending
                })
                .ToArray());
}
