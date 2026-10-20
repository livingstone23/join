using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.EmailTemplates;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — the render of a template: the variables are replaced by the values of the person, the
/// customer, the lead, the user and the company, and the missing references answer 404.
/// </summary>
public sealed class EmailTemplateRenderTests
{
    [Fact]
    public async Task Render_ReplacesTheVariablesWithTheValuesOfThePersonLeadAndCompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(
            typeId,
            "Bienvenida",
            "Hola {{Persona.Nombre}}",
            "<p>{{Lead.Tema}} — {{Empresa.Nombre}} — {{Usuario.Nombre}} — {{Fecha.Hoy}}</p>");
        var person = await context.AddPersonAsync();
        await context.AddCustomerAsync(person.Id, "C-001");
        var lead = await context.AddLeadAsync(person.Id, "LD-000001", "Renovación");
        await context.AddEmailContactAsync(person.Id, "ana@acme.test");

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, LeadId: lead.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("Hola Ana");
        response.Data.BodyHtml.Should().Contain("Renovación").And.Contain("Acme").And.Contain("Remitente Acme");
        response.Data.BodyHtml.Should().NotContain("{{");
    }

    [Fact]
    public async Task Render_UsesTheCustomerAndLeadCodes()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(
            typeId,
            "Comercial",
            "Cliente {{Cliente.Codigo}} / Lead {{Lead.Codigo}}",
            "<p>{{Persona.Correo}}</p>");
        var person = await context.AddPersonAsync();
        await context.AddCustomerAsync(person.Id, "C-042");
        var lead = await context.AddLeadAsync(person.Id, "LD-000009", "Propuesta");
        await context.AddEmailContactAsync(person.Id, "ana@acme.test");

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, LeadId: lead.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("Cliente C-042 / Lead LD-000009");
        response.Data.BodyHtml.Should().Be("<p>ana@acme.test</p>");
    }

    [Fact]
    public async Task Render_RendersAnEmptyValue_WhenThePersonIsNotACustomerNorHasALead()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(
            typeId,
            "Bienvenida",
            "Cliente [{{Cliente.Codigo}}]",
            "<p>[{{Lead.Codigo}}] [{{Lead.Tema}}]</p>");
        var person = await context.AddPersonAsync();

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("Cliente []");
        response.Data.BodyHtml.Should().Be("<p>[] []</p>");
    }

    [Fact]
    public async Task Render_FailsWhenTheTemplateDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var person = await context.AddPersonAsync();

        var response = await context.SendAsync(new RenderEmailTemplateQuery(Guid.NewGuid(), person.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Render_FailsWhenTheTemplateIsInactive()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId, "Bienvenida", "Hola", "<p>Hola</p>", isActive: false);
        var person = await context.AddPersonAsync();

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Render_FailsWhenThePersonDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, Guid.NewGuid()));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.PersonNotFound);
    }

    [Fact]
    public async Task Render_FailsWhenTheLeadBelongsToAnotherPerson()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        var person = await context.AddPersonAsync();
        var other = await context.AddPersonAsync("Otro", "Persona");
        var lead = await context.AddLeadAsync(other.Id);

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, LeadId: lead.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Render_FailsWhenTheLeadDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        var person = await context.AddPersonAsync();

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, LeadId: Guid.NewGuid()));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Render_UsesTheAlternativeEmailOfThePerson_WhenThereIsNoPrimaryOne()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId, "Bienvenida", "Hola", "<p>{{Persona.Correo}}</p>");
        var person = await context.AddPersonAsync();
        await context.AddEmailContactAsync(
            person.Id,
            "alternativo@acme.test",
            isPrimary: false,
            contactType: JOIN.Domain.Enums.ContactType.AlternativeEmail);

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.BodyHtml.Should().Be("<p>alternativo@acme.test</p>");
    }

    [Fact]
    public async Task Render_UsesTheEmailOfTheRequest_WhenItCarriesOne()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId, "Bienvenida", "Hola", "<p>{{Persona.Correo}}</p>");
        var person = await context.AddPersonAsync();
        await context.AddEmailContactAsync(person.Id, "ana@acme.test");

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, "elegido@acme.test"));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.BodyHtml.Should().Be("<p>elegido@acme.test</p>");
    }

    [Fact]
    public async Task Render_PicksThePrimaryEmail_WhenThePersonHasSeveralContacts()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId, "Bienvenida", "Hola", "<p>{{Persona.Correo}}</p>");
        var person = await context.AddPersonAsync();
        await context.AddEmailContactAsync(person.Id, "alternativo@acme.test", isPrimary: false,
            contactType: JOIN.Domain.Enums.ContactType.AlternativeEmail);
        await context.AddEmailContactAsync(person.Id, "principal@acme.test");

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.BodyHtml.Should().Be("<p>principal@acme.test</p>");
    }

    [Fact]
    public async Task Render_FailsWhenTheLeadIsRequestedAndTheTokenHasNoUser()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id);
        context.CurrentUser.SetupGet(x => x.UserId).Returns("not-a-guid");

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id, LeadId: lead.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task Render_FailsWhenTheCompanyOfTheTokenIsEmpty()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        var person = await context.AddPersonAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new RenderEmailTemplateQuery(templateId, person.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }
}
