using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;

namespace JOIN.Application.UnitTest.UseCases.Common.Emails.Commands.SendPersonEmail;

/// <summary>SPEC 72 rule 6 — shape of the content and the address format of rule 1.</summary>
public sealed class SendPersonEmailCommandValidatorTests
{
    [Fact]
    public void Person_SubjectBodyAndAttachmentKinds()
    {
        var validator = new SendPersonEmailCommandValidator();

        validator.Validate(new SendPersonEmailCommand { PersonId = Guid.NewGuid(), Subject = "Hola", BodyHtml = "<p>x</p>" }).IsValid.Should().BeTrue();
        validator.Validate(new SendPersonEmailCommand { PersonId = Guid.Empty, Subject = "", BodyHtml = "" }).Errors.Select(e => e.PropertyName)
            .Should().BeEquivalentTo(["PersonId", "Subject", "BodyHtml"]);
        validator.Validate(new SendPersonEmailCommand { PersonId = Guid.NewGuid(), Subject = new string('s', 201), BodyHtml = new string('b', 50_001) })
            .Errors.Should().HaveCount(2);
        validator.Validate(new SendPersonEmailCommand
        {
            PersonId = Guid.NewGuid(), Subject = "Hola", BodyHtml = "<p>x</p>",
            Attachments = [new PersonEmailAttachmentInput { SourceKind = 3, SourceId = Guid.Empty }]
        }).Errors.Should().HaveCount(2);
    }

    [Fact]
    public void CustomerAndLead_RequireTheirId()
    {
        new SendCustomerEmailCommandValidator().Validate(new SendCustomerEmailCommand { Subject = "a", BodyHtml = "b" }).Errors.Should().ContainSingle(e => e.PropertyName == "CustomerId");
        new SendLeadEmailCommandValidator().Validate(new SendLeadEmailCommand { Subject = "a", BodyHtml = "b" }).Errors.Should().ContainSingle(e => e.PropertyName == "LeadId");
        new SendLeadEmailCommandValidator().Validate(new SendLeadEmailCommand { LeadId = Guid.NewGuid(), Subject = "a", BodyHtml = "b" }).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("ana@cliente.test", true)]
    [InlineData("  ana@cliente.test  ", true)]
    [InlineData("ana@localhost", false)]
    [InlineData("Ana <ana@cliente.test>", false)]
    [InlineData("no-es-correo", false)]
    [InlineData("ana@.test", false)]
    [InlineData("ana@cliente.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmailAddressValidator_AcceptsOnlyBareAddressesWithADomain(string? value, bool expected) =>
        EmailAddressValidator.IsValid(value).Should().Be(expected);

    [Fact]
    public void EmailPlainText_StripsTagsDecodesAndTruncates()
    {
        EmailPlainText.Extract("<p>Hola&nbsp;<b>Ana</b></p>\n<p>¿Qué tal?</p>", 100).Should().Be("Hola Ana ¿Qué tal?");
        EmailPlainText.Extract("<p>abcdef</p>", 3).Should().Be("abc");
        EmailPlainText.Extract(null, 10).Should().BeEmpty();
    }
}
