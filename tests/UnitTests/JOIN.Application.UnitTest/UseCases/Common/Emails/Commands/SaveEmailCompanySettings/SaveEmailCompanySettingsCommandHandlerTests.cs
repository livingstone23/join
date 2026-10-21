using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Services.Email;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UnitTest.UseCases.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;
using JOIN.Application.UseCases.Common.Emails.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.Emails.Commands.SaveEmailCompanySettings;

/// <summary>SPEC 72 rule 9 — one live row of sending rules per company: GET, POST (409 when it exists) and PUT.</summary>
public sealed class SaveEmailCompanySettingsCommandHandlerTests
{
    private static CreateEmailCompanySettingsCommand Create() => new()
    {
        FromEmail = " ventas@acme.test ", FromName = " Ventas ", MaxEmailsPerUserPerDay = 50, MaxAttachmentsTotalBytes = 15L * 1024 * 1024
    };

    [Fact]
    public async Task Create_ThenASecondOneConflicts()
    {
        using var context = await PersonEmailTestContext.CreateAsync(withSettings: false);

        var created = await context.SendAsync(Create());

        created.IsSuccess.Should().BeTrue(created.Message);
        created.Data!.FromEmail.Should().Be("ventas@acme.test");
        created.Data.FromName.Should().Be("Ventas");
        (await context.SendAsync(Create())).Message.Should().Be(PersonEmailErrorCodes.EmailSettingsAlreadyExists);
    }

    [Fact]
    public async Task Update_ChangesTheRow_AndAnEmptySenderMeansTheGlobalOne()
    {
        using var context = await PersonEmailTestContext.CreateAsync();

        var updated = await context.SendAsync(new UpdateEmailCompanySettingsCommand { FromEmail = "  ", MaxEmailsPerUserPerDay = 10, MaxAttachmentsTotalBytes = 1024 });

        updated.IsSuccess.Should().BeTrue(updated.Message);
        updated.Data!.FromEmail.Should().BeNull();
        updated.Data.MaxEmailsPerUserPerDay.Should().Be(10);

        using var empty = await PersonEmailTestContext.CreateAsync(withSettings: false);
        (await empty.SendAsync(new UpdateEmailCompanySettingsCommand { MaxEmailsPerUserPerDay = 1, MaxAttachmentsTotalBytes = 1 }))
            .Message.Should().Be(PersonEmailErrorCodes.EmailSettingsNotConfigured);
    }

    [Fact]
    public async Task WithoutCompany_AnswersCompanyRequired()
    {
        using var context = await PersonEmailTestContext.CreateAsync();
        context.ActingCompanyId = Guid.Empty;

        (await context.SendAsync(Create())).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
        (await context.SendAsync(new UpdateEmailCompanySettingsCommand())).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }

    [Fact]
    public void Validators_SenderFormatQuotaAndSize()
    {
        var validator = new CreateEmailCompanySettingsCommandValidator();
        validator.Validate(Create()).IsValid.Should().BeTrue();
        validator.Validate(Create() with { FromEmail = null }).IsValid.Should().BeTrue();
        validator.Validate(Create() with { FromEmail = "no-es-correo" }).IsValid.Should().BeFalse();
        validator.Validate(Create() with { FromName = new string('n', 151) }).IsValid.Should().BeFalse();
        validator.Validate(Create() with { MaxEmailsPerUserPerDay = 0, MaxAttachmentsTotalBytes = 31L * 1024 * 1024 }).Errors.Should().HaveCount(2);
        new UpdateEmailCompanySettingsCommandValidator().Validate(new UpdateEmailCompanySettingsCommand()).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ReturnsTheRow_OrNotConfigured()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        var sql = new FakeSqlConnectionFactory();
        var handler = new GetEmailCompanySettingsQueryHandler(sql, user.Object);

        sql.SetResults(FakeResultSet.FromRows(new Dictionary<string, object?>
        {
            ["Id"] = Guid.NewGuid(), ["CompanyId"] = Guid.NewGuid(), ["FromEmail"] = null, ["FromName"] = null,
            ["MaxEmailsPerUserPerDay"] = 50, ["MaxAttachmentsTotalBytes"] = 15L * 1024 * 1024
        }));
        (await handler.Handle(new GetEmailCompanySettingsQuery(), CancellationToken.None)).Data!.MaxEmailsPerUserPerDay.Should().Be(50);

        sql.SetResults(FakeResultSet.Empty("Id"));
        (await handler.Handle(new GetEmailCompanySettingsQuery(), CancellationToken.None)).Message.Should().Be(PersonEmailErrorCodes.EmailSettingsNotConfigured);

        user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetEmailCompanySettingsQuery(), CancellationToken.None)).Message.Should().Be(PersonEmailErrorCodes.CompanyRequired);
    }
}
