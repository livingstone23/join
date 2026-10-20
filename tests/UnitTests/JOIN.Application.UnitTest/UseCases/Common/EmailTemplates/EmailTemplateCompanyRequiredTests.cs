using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.EmailTemplates;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — every handler short-circuits with <c>COMPANY_REQUIRED</c> when the token has no
/// company, and the renderer falls back to the alternative e-mail of the person.
/// </summary>
public sealed class EmailTemplateCompanyRequiredTests
{
    [Fact]
    public async Task UpdateType_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new UpdateEmailTemplateTypeCommand
        {
            Id = typeId,
            Code = "INTRO",
            Name = "Presentación"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task DeleteType_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new DeleteEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task RestoreType_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.IsSuperAdmin = true;
        var typeId = await context.AddTypeAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new RestoreEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task ListTypes_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new GetEmailTemplateTypesQuery());

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task UpdateTemplate_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new UpdateEmailTemplateCommand
        {
            Id = templateId,
            EmailTemplateTypeId = typeId,
            Name = "Bienvenida",
            Subject = "Hola",
            BodyHtml = "<p>Hola</p>"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task DeleteTemplate_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new DeleteEmailTemplateCommand(templateId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task RestoreTemplate_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.IsSuperAdmin = true;
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new RestoreEmailTemplateCommand(templateId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Detail_FailsWithoutACompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        var templateId = await context.AddTemplateAsync(typeId);
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new GetEmailTemplateByIdQuery(templateId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }
}
