using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Queries;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using LeadStatus = JOIN.Domain.Customers.Leads.LeadStatus;

namespace JOIN.Application.UnitTest.UseCases.Common.Emails.Queries.GetPersonEmails;

/// <summary>
/// SPEC 72 C.10–C.12 — the Dapper reads: valid addresses, attachment candidates, the history by person, customer and
/// lead, and the detail. The SQL itself runs against PostgreSQL in the integration tests.
/// </summary>
public sealed class GetPersonEmailsQueryHandlerTests
{
    private static FakeResultSet Exists() => FakeResultSet.FromRows(new Dictionary<string, object?> { ["Value"] = 1 });

    private static FakeResultSet LeadScope(Guid ownerId) => FakeResultSet.FromRows(new Dictionary<string, object?>
    {
        ["Status"] = (int)LeadStatus.Open, ["OwnerUserId"] = ownerId, ["SalesTeamId"] = null
    });

    private static Dictionary<string, object?> ListRow(Guid id) => new()
    {
        ["Id"] = id, ["SentAt"] = DateTime.UtcNow, ["Subject"] = "Hola", ["ToAddress"] = "ana@cliente.test", ["Status"] = 1, ["Origin"] = 1,
        ["LeadId"] = null, ["LeadCode"] = null, ["SentByUserId"] = Guid.NewGuid(), ["SentByUserName"] = "Vera Vendedora ", ["AttachmentCount"] = 2
    };

    [Fact]
    public async Task EmailAddresses_KeepOnlyTheValidOnes_PrimaryFirst()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.Sql.SetResults(Exists(), FakeResultSet.FromRows(
            new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Value"] = "alterno@cliente.test", ["IsPrimary"] = false, ["ContactType"] = (int)ContactType.AlternativeEmail },
            new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Value"] = "mal formado", ["IsPrimary"] = false, ["ContactType"] = (int)ContactType.PrimaryEmail },
            new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["Value"] = " ana@cliente.test ", ["IsPrimary"] = true, ["ContactType"] = (int)ContactType.PrimaryEmail }));

        var response = await context.SendAsync(new GetPersonEmailAddressesQuery(Guid.NewGuid()));

        response.Data!.Select(a => a.Value).Should().Equal("ana@cliente.test", "alterno@cliente.test");
        response.Data[0].ContactType.Should().Be("PrimaryEmail");
    }

    [Fact]
    public async Task EmailAddresses_Errors()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.Sql.SetResults(FakeResultSet.Empty("Value"));
        (await context.SendAsync(new GetPersonEmailAddressesQuery(Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.PersonNotFound);

        context.ActingCompanyId = Guid.Empty;
        (await context.SendAsync(new GetPersonEmailAddressesQuery(Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task AttachmentCandidates_ProductDocumentsWithSearch_AndTheLeadDocuments()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var product = new Dictionary<string, object?>
        {
            ["SourceKind"] = "ProductDocument", ["SourceId"] = Guid.NewGuid(), ["FileName"] = "ficha.pdf", ["ContentType"] = "application/pdf",
            ["SizeBytes"] = 3L, ["OriginName"] = "CRM — Ficha"
        };
        var lead = new Dictionary<string, object?>(product) { ["SourceKind"] = "LeadDocument", ["OriginName"] = "LD-1" };
        context.Sql.SetResults(
            Exists(),
            FakeResultSet.FromRows(product),
            LeadScope(context.CurrentUserId),
            FakeResultSet.FromRows(lead),
            FakeResultSet.FromRows(new Dictionary<string, object?> { ["MaxAttachmentsTotalBytes"] = 8L * 1024 * 1024 }));

        var response = await context.SendAsync(new GetPersonEmailAttachmentCandidatesQuery(Guid.NewGuid(), Guid.NewGuid(), " crm "));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Items.Select(c => c.SourceKind).Should().Equal("ProductDocument", "LeadDocument");
        response.Data.MaxAttachmentsTotalBytes.Should().Be(8L * 1024 * 1024);
        context.Sql.CapturedParameters["Search"].Should().Be("%crm%");
    }

    [Fact]
    public async Task AttachmentCandidates_WithoutTheCompanySettings_UsesTheDefaultLimit()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.Sql.SetResults(
            Exists(),
            FakeResultSet.Empty("SourceKind"),
            FakeResultSet.Empty("MaxAttachmentsTotalBytes"));

        var response = await context.SendAsync(new GetPersonEmailAttachmentCandidatesQuery(Guid.NewGuid()));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Items.Should().BeEmpty();
        response.Data.MaxAttachmentsTotalBytes.Should().Be(EmailCompanySettings.DefaultMaxAttachmentsTotalBytes);
    }

    [Fact]
    public async Task AttachmentCandidates_Errors()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.Sql.SetResults(Exists(), FakeResultSet.Empty("SourceKind"), FakeResultSet.Empty("Status"));
        (await context.SendAsync(new GetPersonEmailAttachmentCandidatesQuery(Guid.NewGuid(), Guid.NewGuid())))
            .Message.Should().Be(PersonEmailErrorCodes.LeadNotFound);

        context.Sql.SetResults(FakeResultSet.Empty("Value"));
        (await context.SendAsync(new GetPersonEmailAttachmentCandidatesQuery(Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.PersonNotFound);

        context.ActingCompanyId = Guid.Empty;
        (await context.SendAsync(new GetPersonEmailAttachmentCandidatesQuery(Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }

    [Theory]
    [InlineData(PersonEmailHistoryScope.Person, "m.PersonId = @FilterId")]
    [InlineData(PersonEmailHistoryScope.Customer, "m.PersonId = @FilterId")]
    [InlineData(PersonEmailHistoryScope.Lead, "m.LeadId = @FilterId")]
    public async Task History_FiltersByTheScope_AndPages(PersonEmailHistoryScope scope, string filter)
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var personId = Guid.NewGuid();
        var scopeSet = scope switch
        {
            PersonEmailHistoryScope.Person => Exists(),
            PersonEmailHistoryScope.Customer => FakeResultSet.FromRows(new Dictionary<string, object?> { ["PersonId"] = personId }),
            _ => LeadScope(context.CurrentUserId)
        };
        context.Sql.SetResults(scopeSet, FakeResultSet.FromRows(ListRow(Guid.NewGuid())), FakeResultSet.FromScalar(1));

        var response = await context.SendAsync(new GetPersonEmailsQuery(scope, Guid.NewGuid(), 1, 10));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Single().SentByUserName.Should().Be("Vera Vendedora");
        response.Data.Items.Single().AttachmentCount.Should().Be(2);
        context.Sql.LastCommandText.Should().Contain(filter).And.Contain("m.GcRecord = 0").And.Contain("ORDER BY m.SentAt DESC");
        if (scope == PersonEmailHistoryScope.Customer)
        {
            context.Sql.CapturedParameters["FilterId"].Should().Be(personId, "the history of a customer is the one of its person");
        }
    }

    [Theory]
    [InlineData(PersonEmailHistoryScope.Person, "PERSON_NOT_FOUND")]
    [InlineData(PersonEmailHistoryScope.Customer, "CUSTOMER_NOT_FOUND")]
    [InlineData(PersonEmailHistoryScope.Lead, "LEAD_NOT_FOUND")]
    public async Task History_UnknownScope_Answers404(PersonEmailHistoryScope scope, string code)
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.Sql.SetResults(FakeResultSet.Empty("Value"));

        (await context.SendAsync(new GetPersonEmailsQuery(scope, Guid.NewGuid()))).Message.Should().Be(code);
        (await context.SendAsync(new GetPersonEmailQuery(scope, Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(code);

        context.ActingCompanyId = Guid.Empty;
        (await context.SendAsync(new GetPersonEmailsQuery(scope, Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
        (await context.SendAsync(new GetPersonEmailQuery(scope, Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Detail_ReturnsTheBodyCopiesAndAttachments_OrNotFound()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var emailId = Guid.NewGuid();
        var row = new Dictionary<string, object?>
        {
            ["Id"] = emailId, ["PersonId"] = Guid.NewGuid(), ["CustomerId"] = null, ["LeadId"] = null, ["LeadInteractionId"] = null,
            ["EmailTemplateId"] = null, ["Origin"] = 1, ["ToAddress"] = "ana@cliente.test", ["CcAddresses"] = "a@x.test, b@x.test",
            ["FromAddress"] = "ventas@acme.test", ["ReplyToAddress"] = "vendedor@acme.test", ["Subject"] = "Hola", ["BodyHtml"] = "<p>Hola</p>",
            ["Status"] = 1, ["ProviderMessageId"] = "sg-1", ["ErrorMessage"] = null, ["SentByUserId"] = Guid.NewGuid(),
            ["SentByUserName"] = "Vera ", ["SentAt"] = DateTime.UtcNow
        };
        var attachment = new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["SourceKind"] = 1, ["SourceId"] = Guid.NewGuid(), ["FileName"] = "ficha.pdf", ["ContentType"] = "application/pdf", ["SizeBytes"] = 3L
        };
        context.Sql.SetResults(Exists(), FakeResultSet.FromRows(row), FakeResultSet.FromRows(attachment));

        var response = await context.SendAsync(new GetPersonEmailQuery(PersonEmailHistoryScope.Person, Guid.NewGuid(), emailId));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.CcAddresses.Should().Equal("a@x.test", "b@x.test");
        response.Data.SentByUserName.Should().Be("Vera");
        response.Data.Attachments.Single().SourceKind.Should().Be("ProductDocument");

        context.Sql.SetResults(Exists(), FakeResultSet.Empty("Id"), FakeResultSet.Empty("Id"));
        (await context.SendAsync(new GetPersonEmailQuery(PersonEmailHistoryScope.Person, Guid.NewGuid(), emailId)))
            .Message.Should().Be(PersonEmailErrorCodes.PersonEmailNotFound);
    }
}
