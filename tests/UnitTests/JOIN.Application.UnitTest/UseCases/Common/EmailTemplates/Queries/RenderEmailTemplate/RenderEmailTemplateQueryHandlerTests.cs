using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;
using JOIN.Application.UseCases.Common.EmailTemplates.Queries;
using LeadStatus = JOIN.Domain.Customers.Leads.LeadStatus;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates.Queries.RenderEmailTemplate;

/// <summary>
/// SPEC 71 B.8 — the render reads with Dapper (template, person context, lead scope and lead) and replaces the
/// closed list of variables; a missing or foreign reference answers 404 and a variable without value is empty.
/// The SQL itself (customer code, e-mail order, company and sender) runs against PostgreSQL in the integration tests.
/// </summary>
public sealed class RenderEmailTemplateQueryHandlerTests
{
    private static FakeResultSet Template(string subject = "Hola {{Persona.Nombre}}", string body = "<p>{{Lead.Tema}} — {{Empresa.Nombre}}</p>") =>
        FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["Name"] = "Bienvenida", ["Subject"] = subject, ["BodyHtml"] = body
        });

    private static FakeResultSet Person(string? commercialName = null, string? customerCode = "C-042", string? email = "ana@acme.test") =>
        FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["FirstName"] = "Ana", ["MiddleName"] = null, ["LastName"] = "López", ["SecondLastName"] = "Ruiz",
            ["CommercialName"] = commercialName, ["CustomerCode"] = customerCode, ["Email"] = email,
            ["CompanyName"] = "Acme & Co", ["UserName"] = "Remitente Acme ", ["UserEmail"] = "remitente@acme.test"
        });

    private static FakeResultSet LeadScope() =>
        FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Status"] = (int)LeadStatus.Open, ["OwnerUserId"] = Guid.NewGuid(), ["SalesTeamId"] = null
        });

    private static FakeResultSet Lead(string code = "LD-000009", string topic = "Renovación") =>
        FakeResultSet.FromRows(new Dictionary<string, object?> { ["Code"] = code, ["Topic"] = topic });

    [Fact]
    public async Task Render_ReplacesEveryVariable_WithThePersonCustomerLeadUserAndCompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.Sql.SetResults(
            Template(
                "{{Persona.Nombre}} {{Cliente.Codigo}} {{Lead.Codigo}}",
                "<p>{{Persona.NombreCompleto}}|{{Persona.Correo}}|{{Lead.Tema}}|{{Usuario.Nombre}}|{{Usuario.Correo}}|{{Empresa.Nombre}}|{{Fecha.Hoy}}</p>"),
            Person(), LeadScope(), Lead());

        var response = await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid(), LeadId: Guid.NewGuid()));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("Ana C-042 LD-000009");
        response.Data.BodyHtml.Should().Be(
            $"<p>Ana López Ruiz|ana@acme.test|Renovación|Remitente Acme|remitente@acme.test|Acme &amp; Co|{DateTime.Now:dd/MM/yyyy}</p>");
        context.Sql.CapturedParameters["PersonId"].Should().NotBeNull();
        context.Sql.CapturedParameters["PrimaryEmail"].Should().Be((int)ContactType.PrimaryEmail);
    }

    [Fact]
    public async Task Render_WithoutLeadOrCustomer_LeavesThoseVariablesEmpty_AndUsesTheCommercialName()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.Sql.SetResults(Template("[{{Cliente.Codigo}}] {{Persona.Nombre}}", "<p>[{{Lead.Codigo}}] [{{Lead.Tema}}] [{{Persona.Correo}}]</p>"),
            Person(commercialName: "Acme SRL", customerCode: null, email: null));

        var response = await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid()));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("[] Acme SRL");
        response.Data.BodyHtml.Should().Be("<p>[] [] []</p>");
    }

    [Fact]
    public async Task Render_UsesTheEmailOfTheRequest_WhenItCarriesOne()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.Sql.SetResults(Template("x", "<p>{{Persona.Correo}}</p>"), Person());

        var response = await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid(), " otra@acme.test "));

        response.Data!.BodyHtml.Should().Be("<p>otra@acme.test</p>");
    }

    [Fact]
    public async Task Render_MissingTemplateOrPerson_Answers404()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        context.Sql.SetResults(FakeResultSet.Empty("Id"));
        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);

        context.Sql.SetResults(Template(), FakeResultSet.Empty("FirstName"));
        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(EmailTemplateErrorCodes.PersonNotFound);
    }

    [Fact]
    public async Task Render_LeadNotVisibleOrOfAnotherPerson_AnswersLeadNotFound()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        // Not visible (or not a live lead of the company): the scope query finds nothing.
        context.Sql.SetResults(Template(), Person(), FakeResultSet.Empty("Status"));
        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid(), LeadId: Guid.NewGuid())))
            .Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);

        // Visible but of another person: the lead query filters by PersonId.
        context.Sql.SetResults(Template(), Person(), LeadScope(), FakeResultSet.Empty("Code"));
        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid(), LeadId: Guid.NewGuid())))
            .Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Render_WithALeadAndNoUserInTheToken_AnswersLeadNotFound()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.CurrentUserId = Guid.Empty;
        context.Sql.SetResults(Template(), Person(), LeadScope(), Lead());

        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid(), LeadId: Guid.NewGuid())))
            .Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Render_WithoutCompany_AnswersCompanyRequired()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.ActingCompanyId = Guid.Empty;

        (await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), Guid.NewGuid()))).Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }
}
