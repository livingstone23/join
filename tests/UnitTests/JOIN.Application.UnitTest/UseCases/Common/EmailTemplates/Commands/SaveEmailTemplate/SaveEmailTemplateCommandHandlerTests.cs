using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.EmailTemplates;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — CRUD of the email templates of the company: the type must be an active type of the company,
/// the name is unique, the variables belong to the closed list and the body is stored sanitized.
/// </summary>
public sealed class EmailTemplateCommandTests
{
    [Fact]
    public async Task Create_StoresTheTemplate_AndSanitizesItsBody()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = typeId,
            Name = " Bienvenida ",
            Subject = " Hola {{Persona.Nombre}} ",
            BodyHtml = "<p>Hola {{Persona.Nombre}}</p><script>alert(1)</script>"
        });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Name.Should().Be("Bienvenida");
        response.Data.Subject.Should().Be("Hola {{Persona.Nombre}}");
        response.Data.EmailTemplateTypeCode.Should().Be("INTRO");
        response.Data.BodyHtml.Should().NotContain("<script");
    }

    [Fact]
    public async Task Create_FailsWhenTheCompanyOfTheTokenIsEmpty()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.ActingCompanyId = Guid.Empty;
        var typeId = await context.AddTypeAsync();

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Create_FailsWhenTheTypeDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = Guid.NewGuid(),
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeNotFound);
    }

    [Fact]
    public async Task Create_FailsWhenTheTypeIsInactive()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync("INTRO", "Presentación", isActive: false);

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeNotFound);
    }

    [Fact]
    public async Task Create_FailsWithTheUnknownVariable()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola {{Persona.Apodo}}",
            BodyHtml = "<p>{{Lead.Invitado}}</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateUnknownVariable);
        response.Errors.Should().ContainSingle().Which.Should().Contain("Persona.Apodo");
    }

    [Fact]
    public async Task Create_FailsWhenTheNameAlreadyExistsInTheCompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        await context.AddTemplateAsync(typeId, "Bienvenida");

        var response = await context.SendAsync(new CreateEmailTemplateCommand
        {
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Update_ChangesTheTemplate_AndKeepsItsName()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId, "Bienvenida");

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = templateId,
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola {{Persona.NombreCompleto}}",
            BodyHtml = "<p>{{Empresa.Nombre}}</p>"
        });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Subject.Should().Be("Hola {{Persona.NombreCompleto}}");
    }

    [Fact]
    public async Task Update_FailsWhenTheTemplateDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = Guid.NewGuid(),
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Update_FailsWhenTheTypeDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = templateId,
            EmailTemplateTypeId = Guid.NewGuid(),
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeNotFound);
    }

    [Fact]
    public async Task Update_FailsWithTheUnknownVariable()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = templateId,
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola {{Persona.Apodo}}",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateUnknownVariable);
    }

    [Fact]
    public async Task Update_FailsWhenAnotherTemplateAlreadyHasTheName()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        await context.AddTemplateAsync(typeId, "Bienvenida");
        var otherId = await context.AddTemplateAsync(typeId, "Seguimiento");

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = otherId,
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Delete_LogsTheTemplateOut()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);

        var response = await context.SendAsync(new DeleteEmailTemplateCommand(templateId));

        response.IsSuccess.Should().BeTrue(response.Message);
        var stored = await context.Db.EmailTemplates.IgnoreQueryFilters().SingleAsync();
        stored.GcRecord.Should().NotBe(0);
    }

    [Fact]
    public async Task Delete_FailsWhenTheTemplateDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        var response = await context.SendAsync(new DeleteEmailTemplateCommand(Guid.NewGuid()));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateNotFound);
    }

    [Fact]
    public async Task Restore_FailsForAUserWithoutTheSuperAdminRole()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        await context.SendAsync(new DeleteEmailTemplateCommand(templateId));

        var response = await context.SendAsync(new RestoreEmailTemplateCommand(templateId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.SuperAdminRequired);
    }

    [Fact]
    public async Task Restore_BringsBackTheTemplate_ForASuperAdmin()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.IsSuperAdmin = true;
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        await context.SendAsync(new DeleteEmailTemplateCommand(templateId));

        var response = await context.SendAsync(new RestoreEmailTemplateCommand(templateId));

        response.IsSuccess.Should().BeTrue(response.Message);
        var stored = await context.Db.EmailTemplates.IgnoreQueryFilters().SingleAsync();
        stored.GcRecord.Should().Be(0);
    }
}
