using FluentAssertions;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.UseCases.Common.EmailTemplates;
using JOIN.Application.UseCases.Common.EmailTemplates.Commands;
using JOIN.Application.UseCases.Common.EmailTemplates.Queries;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Queries;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Common.EmailTemplateTypes.Commands.SaveEmailTemplateType;

/// <summary>
/// SPEC 71 — CRUD of the email template types of the company: the code is normalized and unique,
/// the delete is blocked while there are live templates and the restore is SuperAdmin only.
/// </summary>
public sealed class EmailTemplateTypeCommandTests
{
    [Fact]
    public async Task Create_NormalizesTheCode_AndStoresTheTypeInTheCompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        var response = await context.SendAsync(new CreateEmailTemplateTypeCommand
        {
            Code = " follow up ",
            Name = " Seguimiento ",
            SortOrder = 3
        });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Code.Should().Be("FOLLOWUP");
        response.Data.Name.Should().Be("Seguimiento");
        response.Data.CompanyId.Should().Be(context.CompanyId);

        var stored = await context.Db.EmailTemplateTypes.SingleAsync();
        stored.Code.Should().Be("FOLLOWUP");
    }

    [Fact]
    public async Task Create_FailsWhenTheCompanyOfTheTokenIsEmpty()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.ActingCompanyId = Guid.Empty;

        var response = await context.SendAsync(new CreateEmailTemplateTypeCommand { Code = "INTRO", Name = "Presentación" });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Create_FailsWhenTheCodeAlreadyExistsInTheCompany()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        await context.AddTypeAsync("INTRO");

        var response = await context.SendAsync(new CreateEmailTemplateTypeCommand { Code = "intro", Name = "Otra" });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Update_ChangesTheFieldsOfTheType()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync("INTRO", "Presentación");

        var response = await context.SendAsync(new UpdateEmailTemplateTypeCommand
        {
            Id = typeId,
            Code = "INTRO2",
            Name = "Presentación comercial",
            SortOrder = 7,
            IsActive = false
        });

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Code.Should().Be("INTRO2");
        response.Data.SortOrder.Should().Be(7);
        response.Data.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_FailsWhenTheTypeDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        var response = await context.SendAsync(new UpdateEmailTemplateTypeCommand
        {
            Id = Guid.NewGuid(),
            Code = "INTRO",
            Name = "Presentación"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeNotFound);
    }

    [Fact]
    public async Task Update_FailsWhenAnotherTypeOfTheCompanyAlreadyHasTheCode()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        await context.AddTypeAsync("INTRO");
        var otherId = await context.AddTypeAsync("PROPOSAL");

        var response = await context.SendAsync(new UpdateEmailTemplateTypeCommand
        {
            Id = otherId,
            Code = "INTRO",
            Name = "Propuesta"
        });

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.DuplicateKey);
    }

    [Fact]
    public async Task Delete_LogsTheTypeOut_WhenNoTemplateUsesIt()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();

        var response = await context.SendAsync(new DeleteEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeTrue(response.Message);
        var stored = await context.Db.EmailTemplateTypes.IgnoreQueryFilters().SingleAsync();
        stored.GcRecord.Should().NotBe(0);
    }

    [Fact]
    public async Task Delete_FailsWithInUse_WhenALiveTemplateUsesTheType()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        await context.AddTemplateAsync(typeId);

        var response = await context.SendAsync(new DeleteEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeInUse);
        response.Errors.Should().ContainSingle().Which.Should().Be("Active EmailTemplates: 1");
    }

    [Fact]
    public async Task Delete_FailsWhenTheTypeDoesNotExist()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();

        var response = await context.SendAsync(new DeleteEmailTemplateTypeCommand(Guid.NewGuid()));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.EmailTemplateTypeNotFound);
    }

    [Fact]
    public async Task Restore_FailsForAUserWithoutTheSuperAdminRole()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        var typeId = await context.AddTypeAsync();
        await context.SendAsync(new DeleteEmailTemplateTypeCommand(typeId));

        var response = await context.SendAsync(new RestoreEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be(EmailTemplateErrorCodes.SuperAdminRequired);
    }

    [Fact]
    public async Task Restore_BringsBackTheType_ForASuperAdmin()
    {
        using var context = await EmailTemplateTestContext.CreateAsync();
        context.IsSuperAdmin = true;
        var typeId = await context.AddTypeAsync();
        await context.SendAsync(new DeleteEmailTemplateTypeCommand(typeId));

        var response = await context.SendAsync(new RestoreEmailTemplateTypeCommand(typeId));

        response.IsSuccess.Should().BeTrue(response.Message);
        var stored = await context.Db.EmailTemplateTypes.IgnoreQueryFilters().SingleAsync();
        stored.GcRecord.Should().Be(0);
    }
}
