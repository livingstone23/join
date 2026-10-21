using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.UseCases.Common.Emails;
using JOIN.Application.UseCases.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Emails.Commands.SendPersonEmail;

/// <summary>
/// SPEC 72 — the sending from the person, the customer and the lead (<see cref="PersonEmailSender"/>): destination,
/// lead, quota, attachments, content, sender, delivery and the single save of the history and the lead contact.
/// </summary>
public sealed class SendPersonEmailCommandHandlerTests
{
    private static SendPersonEmailCommand Mail(Guid personId, string subject = "Hola {{Persona.Nombre}}", Guid? leadId = null,
        string? to = null, IReadOnlyList<string>? cc = null, IReadOnlyList<PersonEmailAttachmentInput>? attachments = null, Guid? templateId = null) => new()
    {
        PersonId = personId,
        ToAddress = to,
        Cc = cc ?? [],
        Subject = subject,
        BodyHtml = "<p>Le escribe {{Usuario.Nombre}}</p><script>alert(1)</script>",
        LeadId = leadId,
        Attachments = attachments ?? [],
        EmailTemplateId = templateId
    };

    [Fact]
    public async Task FromThePerson_WithoutLead_SendsToThePrimaryAddress_AndKeepsOnlyThePersonHistory()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        context.SetContext();

        var response = await context.SendAsync(Mail(person.Id, cc: ["copia@acme.test"]));

        response.IsSuccess.Should().BeTrue(response.Message);
        var sent = context.Email.Messages.Should().ContainSingle().Subject;
        sent.To.Should().Be("ana@cliente.test");
        sent.Subject.Should().Be("Hola Ana", "the variables reach the provider replaced");
        sent.HtmlBody.Should().Contain("Vera Vendedora").And.NotContain("<script");
        sent.FromEmail.Should().Be("ventas@acme.test");
        sent.FromName.Should().Be("Ventas Acme");
        sent.ReplyTo.Should().Be("vendedor@acme.test");
        sent.Cc.Should().Equal("copia@acme.test");

        var message = (await context.MessagesAsync()).Single();
        message.Status.Should().Be(PersonEmailStatus.Sent);
        message.Origin.Should().Be(PersonEmailOrigin.Person);
        message.LeadId.Should().BeNull();
        message.ProviderMessageId.Should().Be("sg-123");
        message.CcAddresses.Should().Be("copia@acme.test");
        (await context.Db.LeadInteractions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task WithoutTheSenderOfTheCompany_UsesTheGlobalOne()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var settings = await context.Db.EmailCompanySettings.IgnoreQueryFilters().SingleAsync();
        settings.FromEmail = null;
        settings.FromName = "Ignorado";
        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();
        var person = await context.AddPersonAsync();
        context.SetContext();

        (await context.SendAsync(Mail(person.Id))).IsSuccess.Should().BeTrue();

        context.Email.Messages.Single().FromEmail.Should().Be("no-reply@join.test");
        context.Email.Messages.Single().FromName.Should().Be("JOIN");
        (await context.MessagesAsync()).Single().FromAddress.Should().Be("no-reply@join.test");
    }

    [Fact]
    public async Task WithAnOpenLeadOfThePerson_CreatesTheEmailContact_AndTouchesTheLead()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id);
        context.SetContext(withLead: true);

        var response = await context.SendAsync(Mail(person.Id, "Propuesta", leadId: lead.Id));

        response.IsSuccess.Should().BeTrue(response.Message);
        var interaction = await context.Db.LeadInteractions.IgnoreQueryFilters().SingleAsync();
        interaction.ContactTypeCompanyId.Should().Be(context.EmailContactTypeCompanyId);
        interaction.Direction.Should().Be(InteractionDirection.Outbound);
        interaction.Outcome.Should().Be(InteractionOutcome.Other);
        interaction.Summary.Should().StartWith("Correo: Propuesta — Le escribe Vera Vendedora");
        interaction.PersonEmailMessageId.Should().Be(response.Data!.Id);
        response.Data.LeadInteractionId.Should().Be(interaction.Id);
        var message = (await context.MessagesAsync()).Single();
        message.LeadInteractionId.Should().Be(interaction.Id);
        (await context.Db.Leads.IgnoreQueryFilters().SingleAsync()).LastInteractionAt.Should().Be(message.SentAt);
    }

    [Fact]
    public async Task FromTheLead_AlwaysAssociatesThatLead_WithOriginLead()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id);
        context.SetContext(withLead: true);

        var response = await context.SendAsync(new SendLeadEmailCommand { LeadId = lead.Id, Subject = "Seguimiento", BodyHtml = "<p>Hola</p>" });

        response.IsSuccess.Should().BeTrue(response.Message);
        var message = (await context.MessagesAsync()).Single();
        message.Origin.Should().Be(PersonEmailOrigin.Lead);
        message.LeadId.Should().Be(lead.Id);
        message.PersonId.Should().Be(person.Id);
        (await context.SendAsync(new SendLeadEmailCommand { LeadId = Guid.NewGuid(), Subject = "x", BodyHtml = "<p>x</p>" }))
            .Message.Should().Be(PersonEmailErrorCodes.LeadNotFound);
    }

    [Fact]
    public async Task FromTheCustomer_SendsToItsPerson_WithOriginCustomer()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var customer = await context.AddCustomerAsync(person.Id);
        context.SetContext();

        var response = await context.SendAsync(new SendCustomerEmailCommand { CustomerId = customer.Id, Subject = "Hola", BodyHtml = "<p>Hola</p>" });

        response.IsSuccess.Should().BeTrue(response.Message);
        var message = (await context.MessagesAsync()).Single();
        message.Origin.Should().Be(PersonEmailOrigin.Customer);
        message.CustomerId.Should().Be(customer.Id);
        message.PersonId.Should().Be(person.Id);
        (await context.SendAsync(new SendCustomerEmailCommand { CustomerId = Guid.NewGuid(), Subject = "x", BodyHtml = "<p>x</p>" }))
            .Message.Should().Be(PersonEmailErrorCodes.CustomerNotFound);
        context.ActingCompanyId = Guid.Empty;
        (await context.SendAsync(new SendCustomerEmailCommand { CustomerId = customer.Id, Subject = "x", BodyHtml = "<p>x</p>" }))
            .Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task TheProviderFails_SavesFailed_WithoutContact_AndAnswersSendFailedWithTheRow()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id);
        context.SetContext(withLead: true);
        context.Email.Fail = true;

        var response = await context.SendAsync(Mail(person.Id, leadId: lead.Id));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(PersonEmailErrorCodes.EmailSendFailed);
        var message = (await context.MessagesAsync()).Single();
        response.Data!.Id.Should().Be(message.Id);
        message.Status.Should().Be(PersonEmailStatus.Failed);
        message.ErrorMessage.Should().Contain("550");
        (await context.Db.LeadInteractions.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await context.Db.Leads.IgnoreQueryFilters().SingleAsync()).LastInteractionAt.Should().BeNull();
    }

    [Fact]
    public async Task Destination_MustBeAValidAddressOfThePerson()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync(email: "no-es-correo");
        (await context.SendAsync(Mail(person.Id))).Message.Should().Be(PersonEmailErrorCodes.PersonEmailNotAvailable);

        await context.AddContactAsync(person.Id, "alterno@cliente.test");
        context.SetContext();
        (await context.SendAsync(Mail(person.Id, to: "otro@externo.test"))).Message.Should().Be(PersonEmailErrorCodes.InvalidEmailAddress);
        (await context.SendAsync(Mail(person.Id, cc: ["mal formado"]))).Message.Should().Be(PersonEmailErrorCodes.InvalidEmailAddress);
        (await context.SendAsync(Mail(person.Id, cc: ["a@x.test", "b@x.test", "c@x.test", "d@x.test", "e@x.test", "f@x.test"])))
            .Message.Should().Be(PersonEmailErrorCodes.InvalidEmailAddress);

        (await context.SendAsync(Mail(person.Id, to: " ALTERNO@cliente.test "))).IsSuccess.Should().BeTrue();
        context.Email.Messages.Single().To.Should().Be("alterno@cliente.test");
    }

    [Fact]
    public async Task ThePerson_MustExistAndBeActive()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var inactive = await context.AddPersonAsync(active: false);

        (await context.SendAsync(Mail(inactive.Id))).Message.Should().Be(PersonEmailErrorCodes.PersonNotActive);
        (await context.SendAsync(Mail(Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.PersonNotFound);
        context.CurrentUser.SetupGet(x => x.UserId).Returns((string?)null);
        (await context.SendAsync(Mail(inactive.Id))).Message.Should().Be("USER_REQUIRED");
        context.Email.Messages.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LeadStatus.Won)]
    [InlineData(LeadStatus.Lost)]
    [InlineData(LeadStatus.Inactive)]
    public async Task ALeadThatIsNotOpen_AnswersLeadNotOpen_AndSendsNothing(LeadStatus status)
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id, status);

        (await context.SendAsync(Mail(person.Id, leadId: lead.Id))).Message.Should().Be(PersonEmailErrorCodes.LeadNotOpen);
        context.Email.Messages.Should().BeEmpty();
        (await context.MessagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheLead_MustBeOfThePerson_AndTheCompanyNeedsTheEmailContactType()
    {
        using var context = await PersonEmailTestContext.CreateAsync(withEmailContactType: false);
        var person = await context.AddPersonAsync();
        var other = await context.AddPersonAsync("otra@cliente.test");
        var foreignLead = await context.AddLeadAsync(other.Id);
        var ownLead = await context.AddLeadAsync(person.Id);

        (await context.SendAsync(Mail(person.Id, leadId: foreignLead.Id))).Message.Should().Be(PersonEmailErrorCodes.InvalidLeadPerson);
        (await context.SendAsync(Mail(person.Id, leadId: ownLead.Id))).Message.Should().Be(PersonEmailErrorCodes.EmailContactTypeNotConfigured);
        (await context.SendAsync(Mail(person.Id, leadId: Guid.NewGuid()))).Message.Should().Be(PersonEmailErrorCodes.LeadNotFound);
        context.Email.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutSettings_AnswersNotConfigured_AndTheDailyQuotaIsPerUser()
    {
        using var empty = await PersonEmailTestContext.CreateAsync(withSettings: false);
        var someone = await empty.AddPersonAsync();
        (await empty.SendAsync(Mail(someone.Id))).Message.Should().Be(PersonEmailErrorCodes.EmailSettingsNotConfigured);

        using var context = await PersonEmailTestContext.CreateAsync();
        var settings = await context.Db.EmailCompanySettings.IgnoreQueryFilters().SingleAsync();
        settings.MaxEmailsPerUserPerDay = 2;
        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();
        var person = await context.AddPersonAsync();

        context.SetContext();
        (await context.SendAsync(Mail(person.Id))).IsSuccess.Should().BeTrue();
        context.Email.Fail = true;
        context.SetContext();
        (await context.SendAsync(Mail(person.Id))).Message.Should().Be(PersonEmailErrorCodes.EmailSendFailed, "a Failed send also counts");
        (await context.SendAsync(Mail(person.Id))).Message.Should().Be(PersonEmailErrorCodes.EmailDailyLimitReached);
    }

    [Fact]
    public async Task TheTemplate_MustBeActive_AndTheVariablesKnown()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var type = new EmailTemplateType { CompanyId = context.CompanyId, Code = "INTRO", Name = "Intro", IsActive = true };
        var inactive = new EmailTemplate { CompanyId = context.CompanyId, EmailTemplateTypeId = type.Id, Name = "Vieja", Subject = "s", BodyHtml = "b", IsActive = false };
        var active = new EmailTemplate { CompanyId = context.CompanyId, EmailTemplateTypeId = type.Id, Name = "Nueva", Subject = "s", BodyHtml = "b", IsActive = true };
        context.Db.AddRange(type, inactive, active);
        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();

        (await context.SendAsync(Mail(person.Id, templateId: inactive.Id))).Message.Should().Be(PersonEmailErrorCodes.EmailTemplateNotFound);
        (await context.SendAsync(Mail(person.Id, subject: "Hola {{Persona.Apodo}}"))).Message.Should().Be(PersonEmailErrorCodes.EmailTemplateUnknownVariable);

        context.SetContext();
        (await context.SendAsync(Mail(person.Id, templateId: active.Id))).IsSuccess.Should().BeTrue();
        (await context.MessagesAsync()).Single().EmailTemplateId.Should().Be(active.Id);
    }

    [Fact]
    public async Task Attachments_ComeFromProductAndLeadDocuments_WithinTheLimits()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        var lead = await context.AddLeadAsync(person.Id);
        var otherLead = await context.AddLeadAsync(person.Id);
        var productDocument = await context.AddProductDocumentAsync([1, 2, 3]);
        var leadDocument = await context.AddLeadDocumentAsync(lead.Id, [4, 5]);
        var foreignLeadDocument = await context.AddLeadDocumentAsync(otherLead.Id, [6]);
        PersonEmailAttachmentInput Product(Guid id) => new() { SourceKind = 1, SourceId = id };
        PersonEmailAttachmentInput Lead(Guid id) => new() { SourceKind = 2, SourceId = id };

        (await context.SendAsync(Mail(person.Id, leadId: lead.Id, attachments: [Lead(foreignLeadDocument.Id)])))
            .Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentNotFound);
        (await context.SendAsync(Mail(person.Id, attachments: [Lead(leadDocument.Id)])))
            .Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentNotFound, "a lead document needs the email to be associated to its lead");
        (await context.SendAsync(Mail(person.Id, attachments: [Product(Guid.NewGuid())])))
            .Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentNotFound);
        (await context.SendAsync(Mail(person.Id, attachments: [.. Enumerable.Range(0, 11).Select(_ => Product(Guid.NewGuid()))])))
            .Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentsTooLarge);

        context.SetContext(withLead: true);
        var response = await context.SendAsync(Mail(person.Id, leadId: lead.Id, attachments: [Product(productDocument.Id), Lead(leadDocument.Id)]));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.AttachmentCount.Should().Be(2);
        context.Email.Messages.Single().Attachments.Select(a => a.FileName).Should().BeEquivalentTo(["ficha.pdf", "propuesta.pdf"]);
        context.Email.Messages.Single().Attachments.Sum(a => a.Content.Length).Should().Be(5);
        (await context.Db.PersonEmailMessageAttachments.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Attachments_OverTheTotalSizeOrMissingInStorage_AreRejected()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var settings = await context.Db.EmailCompanySettings.IgnoreQueryFilters().SingleAsync();
        settings.MaxAttachmentsTotalBytes = 4;
        await context.Db.SaveChangesAsync();
        context.Db.ChangeTracker.Clear();
        var person = await context.AddPersonAsync();
        var big = await context.AddProductDocumentAsync([1, 2, 3, 4, 5]);
        var cloud = await context.AddProductDocumentAsync([1], StorageProviderKind.AzureBlob);
        var missing = await context.AddProductDocumentAsync([1]);
        context.Files.Remove(missing.Path);
        var foreign = await context.AddProductDocumentAsync([1], companyId: Guid.NewGuid());
        PersonEmailAttachmentInput Product(Guid id) => new() { SourceKind = 1, SourceId = id };

        (await context.SendAsync(Mail(person.Id, attachments: [Product(big.Id)]))).Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentsTooLarge);
        (await context.SendAsync(Mail(person.Id, attachments: [Product(cloud.Id)]))).Message.Should().Be(PersonEmailErrorCodes.StorageProviderNotConfigured);
        (await context.SendAsync(Mail(person.Id, attachments: [Product(missing.Id)]))).Message.Should().Be(PersonEmailErrorCodes.FileNotFoundInStorage);
        (await context.SendAsync(Mail(person.Id, attachments: [Product(foreign.Id)]))).Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentNotFound);
        (await context.SendAsync(Mail(person.Id, attachments: [new PersonEmailAttachmentInput { SourceKind = 9, SourceId = big.Id }])))
            .Message.Should().Be(PersonEmailErrorCodes.EmailAttachmentNotFound);
        context.Email.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task AContextError_IsReturnedWithoutSending()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        var person = await context.AddPersonAsync();
        context.Sql.SetResults(JOIN.Application.UnitTest.Common.TestDoubles.FakeResultSet.Empty("FirstName"));

        (await context.SendAsync(Mail(person.Id))).Message.Should().Be(PersonEmailErrorCodes.PersonNotFound);
        context.Email.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Summary_StartsWithTheSubject_AndFitsTheContact()
    {
        PersonEmailSender.Summary("Asunto", "<p>Hola &amp; adiós</p>").Should().Be("Correo: Asunto — Hola & adiós");
        PersonEmailSender.Summary("Asunto", "<p></p>").Should().Be("Correo: Asunto");
        PersonEmailSender.Summary("Asunto", $"<p>{new string('x', 5000)}</p>").Length.Should().Be(PersonEmailSender.SummaryMaxLength);
        PersonEmailSender.Summary(new string('s', 2100), "<p>x</p>").Length.Should().Be(PersonEmailSender.SummaryMaxLength);
    }
}
