using System.Text;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.Common.TestDoubles;
using JOIN.Application.UseCases.Customers.Leads.Commands;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Documents;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Customers.Leads;

/// <summary>
/// SPEC 62: lead documents (limits, open leads only, delete/restore keeps the file) and the attachment settings.
/// </summary>
public sealed class LeadDocumentsTests
{
    private static InboundAttachment File(string name, int size = 10) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(new string('x', size))), name, "application/pdf", size);

    private static async Task<(Guid LeadId, Guid InteractionId)> LeadAsync(LeadTestContext ctx, bool settings = true, int perInteraction = 2, int? perDay = 10, long maxSize = 100)
    {
        var created = await ctx.SendAsync(new CreateLeadCommand
        {
            PersonId = ctx.PersonId, Topic = "T", RelationshipTypeId = ctx.Relationship["PROSPECT"], LeadSourceId = ctx.Source["WEB"],
            FirstInteraction = new LeadInteractionInput { ContactTypeCompanyId = ctx.ContactType["EMAIL"], Summary = "x" }
        });
        if (settings)
        {
            (await ctx.SendAsync(new CreateLeadAttachmentSettingsCommand
            {
                AllowedDocumentTypes = (int)(DocumentType.Pdf | DocumentType.Image), MaxFileSizeBytes = maxSize, MaxFilesPerInteraction = perInteraction, MaxFilesPerDay = perDay
            })).IsSuccess.Should().BeTrue();
        }

        var interaction = (await ctx.RowsAsync<LeadInteraction>(i => i.LeadId == created.Data!.Lead.Id)).Single();
        return (created.Data!.Lead.Id, interaction.Id);
    }

    [Fact]
    public async Task Upload_StoresTheFileWithTheProvider_AndListsItUnderTheContact()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (leadId, interactionId) = await LeadAsync(ctx);

        var uploaded = await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, LeadInteractionId = interactionId, File = File("Propuesta.PDF") });

        uploaded.IsSuccess.Should().BeTrue(uploaded.Message);
        uploaded.Data!.DocumentType.Should().Be("Pdf");
        uploaded.Data.UploadedByUserName.Should().Be("ana User");
        var row = (await ctx.RowsAsync<LeadDocument>(d => d.Id == uploaded.Data.Id)).Single();
        row.StorageProvider.Should().Be(StorageProviderKind.Local);
        row.Path.Should().StartWith($"leads/{ctx.CompanyId}/{leadId}/");
        ctx.Storage.Files.Should().ContainKey(row.Path);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("foto.png") })).IsSuccess.Should().BeTrue("without a contact");
    }

    [Fact]
    public async Task Upload_EachLimitAnswersItsCode()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (leadId, interactionId) = await LeadAsync(ctx, perInteraction: 1, perDay: 2);

        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("hoja.xlsx") })).Message.Should().Be(LeadErrorCodes.DocumentTypeNotAllowed);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("virus.exe") })).Message.Should().Be(LeadErrorCodes.DocumentTypeNotAllowed);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("big.pdf", 101) })).Message.Should().Be(LeadErrorCodes.DocumentTooLarge);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, LeadInteractionId = Guid.NewGuid(), File = File("a.pdf") })).Message.Should().Be(LeadErrorCodes.InvalidLeadInteraction);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, LeadInteractionId = interactionId, File = File("a.pdf") })).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, LeadInteractionId = interactionId, File = File("b.pdf") })).Message.Should().Be(LeadErrorCodes.DocumentLimitPerInteraction);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("c.pdf") })).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("d.pdf") })).Message.Should().Be(LeadErrorCodes.DocumentLimitPerDay);
    }

    [Fact]
    public async Task Upload_NeedsSettings_AnOpenLead_AndAVisibleLead()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (leadId, _) = await LeadAsync(ctx, settings: false);

        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("a.pdf") })).Message.Should().Be(LeadErrorCodes.AttachmentSettingsNotConfigured);
        await ctx.SendAsync(new CreateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 1, MaxFileSizeBytes = 100, MaxFilesPerInteraction = 1 });

        ctx.ActAs(ctx.OtherUserId);
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("a.pdf") })).Message.Should().Be(LeadErrorCodes.LeadNotFound);

        ctx.ActAs(ctx.UserId);
        await ctx.SendAsync(new InactivateLeadCommand { Id = leadId, LeadReasonId = ctx.Reason["NO_ANSWER"] });
        (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("a.pdf") })).Message.Should().Be(LeadErrorCodes.LeadNotOpen);
    }

    [Fact]
    public async Task DeleteAndRestore_KeepTheFile()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var (leadId, _) = await LeadAsync(ctx);
        var document = (await ctx.SendAsync(new UploadLeadDocumentCommand { LeadId = leadId, File = File("a.pdf") })).Data!;
        var path = (await ctx.RowsAsync<LeadDocument>(d => d.Id == document.Id)).Single().Path;

        (await ctx.SendAsync(new DeleteLeadDocumentCommand(leadId, document.Id))).IsSuccess.Should().BeTrue();
        (await ctx.SendAsync(new DeleteLeadDocumentCommand(leadId, document.Id))).Message.Should().Be(LeadErrorCodes.DocumentNotFound);
        ctx.Storage.Files.Should().ContainKey(path);

        (await ctx.SendAsync(new RestoreLeadDocumentCommand(leadId, document.Id))).Message.Should().Be(LeadErrorCodes.SuperAdminRequired);
        ctx.IsSuperAdmin = true;
        (await ctx.SendAsync(new RestoreLeadDocumentCommand(Guid.NewGuid(), document.Id))).Message.Should().Be("PARENT_DELETED", "another lead");
        (await ctx.SendAsync(new RestoreLeadDocumentCommand(leadId, document.Id))).IsSuccess.Should().BeTrue();
        (await ctx.RowsAsync<LeadDocument>(d => d.Id == document.Id)).Single().GcRecord.Should().Be(0);

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(new RestoreLeadDocumentCommand(leadId, document.Id))).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }

    [Fact]
    public async Task Settings_CreateOnce_ThenUpdate()
    {
        using var ctx = await LeadTestContext.CreateAsync();
        var create = new CreateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 17, MaxFileSizeBytes = 1000, MaxFilesPerInteraction = 3, MaxFilesPerDay = 50 };

        (await ctx.SendAsync(new UpdateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 1, MaxFileSizeBytes = 1, MaxFilesPerInteraction = 1 })).Message.Should().Be(LeadErrorCodes.AttachmentSettingsNotConfigured);
        var created = await ctx.SendAsync(create);
        created.Data!.AllowedDocumentTypeNames.Should().Equal("Pdf", "Image");
        (await ctx.SendAsync(create)).Message.Should().Be("DUPLICATE_KEY");
        var updated = await ctx.SendAsync(new UpdateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 2, MaxFileSizeBytes = 5, MaxFilesPerInteraction = 1, MaxFilesPerDay = null });
        updated.Data!.AllowedDocumentTypeNames.Should().Equal("Word");
        updated.Data.MaxFilesPerDay.Should().BeNull();

        ctx.ActingCompanyId = Guid.Empty;
        (await ctx.SendAsync(create)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateLeadAttachmentSettingsCommand())).Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }

    [Fact]
    public void Validators_RejectInvalidInput()
    {
        new CreateLeadAttachmentSettingsCommandValidator().Validate(new CreateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 0, MaxFileSizeBytes = 0, MaxFilesPerInteraction = 0, MaxFilesPerDay = 0 })
            .Errors.Should().HaveCount(4);
        new UpdateLeadAttachmentSettingsCommandValidator().Validate(new UpdateLeadAttachmentSettingsCommand { AllowedDocumentTypes = 128, MaxFileSizeBytes = 1, MaxFilesPerInteraction = 1 })
            .IsValid.Should().BeFalse();
        new UploadLeadDocumentCommandValidator().Validate(new UploadLeadDocumentCommand()).IsValid.Should().BeFalse();
        new UploadLeadDocumentCommandValidator().Validate(new UploadLeadDocumentCommand { LeadId = Guid.NewGuid(), File = new InboundAttachment(Stream.Null, "", "", 0) })
            .Errors.Should().HaveCount(3);
    }

    [Fact]
    public async Task Queries_ApplyVisibility_AndDownloadUsesTheRowProvider()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CompanyId).Returns(companyId);
        user.SetupGet(x => x.UserId).Returns(userId.ToString());
        var guard = new LeadAccessGuard(new LeadTestContext.FakeScopes(), user.Object);
        var storage = new LeadTestContext.FakeStorage();
        storage.Files["k"] = [1, 2];
        Dictionary<string, object?> Visible() => new() { ["Status"] = 1, ["OwnerUserId"] = userId, ["SalesTeamId"] = null };

        var hidden = new FakeSqlConnectionFactory();
        hidden.SetResults(FakeResultSet.Empty("Status"));
        (await new GetLeadDocumentsQueryHandler(hidden, user.Object, guard).Handle(new GetLeadDocumentsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.LeadNotFound);
        (await new DownloadLeadDocumentQueryHandler(hidden, user.Object, guard, storage).Handle(new DownloadLeadDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.DocumentNotFound);

        var list = new FakeSqlConnectionFactory();
        list.SetResults(FakeResultSet.FromRows(Visible()), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["DocumentType"] = 1, ["OriginalName"] = "a.pdf" }));
        var docs = await new GetLeadDocumentsQueryHandler(list, user.Object, guard).Handle(new GetLeadDocumentsQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);
        docs.Data!.Single().DocumentType.Should().Be("Pdf");
        list.LastCommandText.Should().Contain("d.LeadInteractionId = @LeadInteractionId").And.Contain("d.GcRecord = 0");

        var download = new FakeSqlConnectionFactory();
        download.SetResults(FakeResultSet.FromRows(Visible()), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Path"] = "k", ["StorageProvider"] = (int)StorageProviderKind.Local, ["ContentType"] = "application/pdf", ["OriginalName"] = "a.pdf" }));
        (await new DownloadLeadDocumentQueryHandler(download, user.Object, guard, storage).Handle(new DownloadLeadDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Data!.OriginalName.Should().Be("a.pdf");

        var otherProvider = new FakeSqlConnectionFactory();
        otherProvider.SetResults(FakeResultSet.FromRows(Visible()), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Path"] = "k", ["StorageProvider"] = (int)StorageProviderKind.AzureBlob, ["ContentType"] = "x", ["OriginalName"] = "a" }));
        (await new DownloadLeadDocumentQueryHandler(otherProvider, user.Object, guard, storage).Handle(new DownloadLeadDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.StorageProviderNotConfigured);

        storage.Missing = true;
        var missing = new FakeSqlConnectionFactory();
        missing.SetResults(FakeResultSet.FromRows(Visible()), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Path"] = "k", ["StorageProvider"] = (int)StorageProviderKind.Local, ["ContentType"] = "x", ["OriginalName"] = "a" }));
        (await new DownloadLeadDocumentQueryHandler(missing, user.Object, guard, storage).Handle(new DownloadLeadDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.FileNotFoundInStorage);

        var settings = new FakeSqlConnectionFactory();
        settings.SetResults(FakeResultSet.Empty("Id"), FakeResultSet.FromRows(new Dictionary<string, object?> { ["Id"] = Guid.NewGuid(), ["CompanyId"] = companyId, ["AllowedDocumentTypes"] = 5, ["MaxFileSizeBytes"] = 10L, ["MaxFilesPerInteraction"] = 5, ["MaxFilesPerDay"] = null }));
        var handler = new GetLeadAttachmentSettingsQueryHandler(settings, user.Object);
        (await handler.Handle(new GetLeadAttachmentSettingsQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.AttachmentSettingsNotConfigured);
        (await handler.Handle(new GetLeadAttachmentSettingsQuery(), CancellationToken.None)).Data!.AllowedDocumentTypeNames.Should().Equal("Pdf", "Excel");

        user.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        (await handler.Handle(new GetLeadAttachmentSettingsQuery(), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new GetLeadDocumentsQueryHandler(hidden, user.Object, guard).Handle(new GetLeadDocumentsQuery(Guid.NewGuid()), CancellationToken.None)).Message.Should().Be(LeadErrorCodes.CompanyRequired);
        (await new DownloadLeadDocumentQueryHandler(hidden, user.Object, guard, storage).Handle(new DownloadLeadDocumentQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None))
            .Message.Should().Be(LeadErrorCodes.CompanyRequired);
    }
}
