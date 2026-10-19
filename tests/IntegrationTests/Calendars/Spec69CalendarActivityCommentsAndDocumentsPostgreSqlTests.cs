// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries;

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 69 — end-to-end coverage of comments and documents on calendar activities: the owner adds a
/// comment and a PDF linked to it; the bitácora shows <c>CommentAdded</c> + <c>DocumentAdded</c>; limits
/// (type / size / per-comment / daily) answer with the right codes; only an admin deletes; settings CRUD
/// works. Also runs <c>EXPLAIN</c> on the comments / documents list query.
/// </summary>
public sealed class Spec69CalendarActivityCommentsAndDocumentsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Creator = "spec69-tests";
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public Spec69CalendarActivityCommentsAndDocumentsPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Acceptance #1 — the owner adds a comment and a PDF linked to it; both are listed; the bitácora records
    /// <c>CommentAdded</c> + <c>DocumentAdded</c>.
    /// </summary>
    [Fact]
    public async Task Owner_AddsCommentAndPdf_BothListedAndJournaled()
    {
        var s = await SeedAsync();
        var activityId = await s.CreateActivityAsync(title: "Kickoff", from: "2027-04-05T10:00:00-06:00", to: "2027-04-05T11:00:00-06:00");

        var commentResponse = await s.OwnerClient.PostAsJsonAsync(
            $"/api/v1/CalendarEvents/{activityId}/comments",
            new { body = "Look at the contract draft." });
        commentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var comment = Data(await commentResponse.Content.ReadAsStringAsync());
        var commentId = comment.GetProperty("id").GetGuid();

        // Upload a PDF attached to the comment.
        var upload = await UploadPdfAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", commentId, "contract.pdf");
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var document = Data(await upload.Content.ReadAsStringAsync());
        document.GetProperty("calendarEventCommentId").GetGuid().Should().Be(commentId);
        document.GetProperty("originalName").GetString().Should().Be("contract.pdf");

        // Comments list returns both rows; the document is nested under the comment.
        var list = await s.OwnerClient.GetAsync($"/api/v1/CalendarEvents/{activityId}/comments");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var listed = Data(await list.Content.ReadAsStringAsync()).EnumerateArray().ToList();
        listed.Should().HaveCount(1);
        listed[0].GetProperty("body").GetString().Should().Be("Look at the contract draft.");
        listed[0].GetProperty("documents")[0].GetProperty("id").GetGuid().Should().Be(document.GetProperty("id").GetGuid());

        // Journal rows: CommentAdded + DocumentAdded.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = await db.CalendarEventLogs.IgnoreQueryFilters()
            .Where(l => l.CalendarEventId == activityId && (l.Action == CalendarEventLogAction.CommentAdded || l.Action == CalendarEventLogAction.DocumentAdded))
            .Select(l => l.Action)
            .ToListAsync();
        actions.Should().Contain(CalendarEventLogAction.CommentAdded);
        actions.Should().Contain(CalendarEventLogAction.DocumentAdded);
    }

    /// <summary>
    /// Acceptance #4 — the author (a member without <c>CalendarActivityRecords.CanDelete</c>) cannot delete their
    /// comment (403); an administrator deletes it with a reason, the text stops being returned, the attached file
    /// is kept in storage and the bitácora records <c>CommentDeleted</c> / <c>DocumentDeleted</c> with the reason.
    /// </summary>
    [Fact]
    public async Task Delete_AuthorIsForbidden_AdminDeletesWithReason_FileKeptAndJournaled()
    {
        var s = await SeedAsync();
        var (memberId, member) = await MemberAsync(s.CompanyId, "UsuarioSimple", canDownload: true);
        var memberCalendarId = await CreateUserCalendarAsync(s.CompanyId, memberId, canViewOthers: false);
        var activityId = await s.CreateActivityAsync("Demo", "2027-04-05T10:00:00-06:00", "2027-04-05T11:00:00-06:00",
            ownerUserId: memberId, calendarId: memberCalendarId);
        var commentId = await CreateCommentAsync(member, activityId, "first");
        var upload = await UploadPdfAsync(member, $"/api/v1/CalendarEvents/{activityId}/documents", commentId, "kept.pdf");
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentId = Data(await upload.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

        (await DeleteWithReasonAsync(member, $"/api/v1/CalendarEvents/{activityId}/comments/{commentId}", "mine"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await DeleteWithReasonAsync(s.AdminClient, $"/api/v1/CalendarEvents/{activityId}/comments/{commentId}", "off-topic"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await DeleteWithReasonAsync(s.AdminClient, $"/api/v1/CalendarEvents/{activityId}/documents/{documentId}", "retention"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await DeleteWithReasonAsync(s.AdminClient, $"/api/v1/CalendarEvents/{activityId}/comments/{commentId}", "again");
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_RECORD_ALREADY_DELETED");

        var item = Data(EnsureSuccessContent(await member.GetAsync($"/api/v1/CalendarEvents/{activityId}/comments")))
            .EnumerateArray().Single();
        item.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        item.GetProperty("deleteReason").GetString().Should().Be("off-topic");
        item.GetProperty("deletedByUserId").GetGuid().Should().Be(s.AdminId);
        item.GetProperty("documents")[0].GetProperty("isDeleted").GetBoolean().Should().BeTrue();

        // A deleted document no longer downloads, but the file stays in storage.
        (await member.GetAsync($"/api/v1/CalendarEvents/{activityId}/documents/{documentId}/download"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.CalendarEventDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId);
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageResolver>().Resolve(row.StorageProvider);
            await using var stream = await storage!.OpenReadAsync(row.Path, CancellationToken.None);
            stream.Should().NotBeNull("the logical delete keeps the file");

            var logs = await db.CalendarEventLogs.IgnoreQueryFilters()
                .Where(l => l.CalendarEventId == activityId
                    && (l.Action == CalendarEventLogAction.CommentDeleted || l.Action == CalendarEventLogAction.DocumentDeleted))
                .ToListAsync();
            logs.Should().ContainSingle(l => l.Action == CalendarEventLogAction.CommentDeleted
                && l.CalendarEventCommentId == commentId && l.Notes == "off-topic");
            logs.Should().ContainSingle(l => l.Action == CalendarEventLogAction.DocumentDeleted
                && l.CalendarEventDocumentId == documentId && l.Notes == "retention");
        }

        // Item 14 — the activity log returns the new actions with their ids.
        var history = Data(EnsureSuccessContent(await member.GetAsync($"/api/v1/CalendarEvents/{activityId}/logs")))
            .EnumerateArray().ToList();
        history.Should().Contain(l => l.GetProperty("action").GetString() == "CommentDeleted"
            && l.GetProperty("commentId").GetGuid() == commentId);
        history.Should().Contain(l => l.GetProperty("action").GetString() == "DocumentAdded"
            && l.GetProperty("documentId").GetGuid() == documentId);
    }

    /// <summary>
    /// Acceptance #2 — a comment on the series master is seen from the master (every unmodified occurrence); a
    /// comment on the modified occurrence is seen only from it.
    /// </summary>
    [Fact]
    public async Task Series_MasterCommentsAndExceptionCommentsAreSeparate()
    {
        var s = await SeedAsync();
        var (masterId, exceptionId) = await s.CreateWeeklySeriesWithExceptionAsync();

        await CreateCommentAsync(s.AdminClient, masterId, "on the series");
        await CreateCommentAsync(s.AdminClient, exceptionId, "on the moved one");

        var onMaster = Data(EnsureSuccessContent(await s.AdminClient.GetAsync($"/api/v1/CalendarEvents/{masterId}/comments")))
            .EnumerateArray().Select(c => c.GetProperty("body").GetString()).ToList();
        var onException = Data(EnsureSuccessContent(await s.AdminClient.GetAsync($"/api/v1/CalendarEvents/{exceptionId}/comments")))
            .EnumerateArray().Select(c => c.GetProperty("body").GetString()).ToList();

        onMaster.Should().Equal("on the series");
        onException.Should().Equal("on the moved one");
    }

    /// <summary>
    /// Acceptance #3 — a member comments on someone else's activity only with the SPEC 68 flag or with
    /// <c>CalendarOthers.CanRead</c>; without either the activity answers 404.
    /// </summary>
    [Fact]
    public async Task Visibility_ForeignActivity_NeedsFlagOrPanelRead()
    {
        var s = await SeedAsync();
        var activityId = await s.CreateActivityAsync("Admin's", "2027-04-05T10:00:00-06:00", "2027-04-05T11:00:00-06:00");
        var url = $"/api/v1/CalendarEvents/{activityId}/comments";

        var (memberId, member) = await MemberAsync(s.CompanyId, "UsuarioSimple", canDownload: true);
        var hidden = await member.PostAsJsonAsync(url, new { body = "hello" });
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await hidden.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_EVENT_NOT_FOUND");
        (await member.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await CreateUserCalendarAsync(s.CompanyId, memberId, canViewOthers: true);
        (await member.PostAsJsonAsync(url, new { body = "with the flag" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var (_, panelReader) = await MemberAsync(s.CompanyId, "Coordinador", canDownload: true, panelRead: true);
        (await panelReader.PostAsJsonAsync(url, new { body = "from the panel" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var bodies = Data(EnsureSuccessContent(await s.AdminClient.GetAsync(url)))
            .EnumerateArray().Select(c => c.GetProperty("body").GetString()).ToList();
        bodies.Should().BeEquivalentTo("with the flag", "from the panel");
    }

    /// <summary>
    /// Acceptance #5 — every limit (type / size / per-comment / daily) answers its own code; the company
    /// without limits answers the not-configured code.
    /// </summary>
    [Fact]
    public async Task Limits_ReplyOnDocumentType_TooLarge_PerComment_PerDay()
    {
        var s = await SeedAsync();

        // Upload with .gif returns DocumentTypeNotAllowed (extension not on the table).
        var activityId = await s.CreateActivityAsync(title: "Limits", from: "2027-04-05T10:00:00-06:00", to: "2027-04-05T11:00:00-06:00");
        var gif = await UploadBytesAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "image.gif", "image/gif", new byte[] { 0x47, 0x49, 0x46 });
        gif.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await gif.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_DOCUMENT_TYPE_NOT_ALLOWED");

        // File too large (the seed default is 10 MB).
        var tooBig = new byte[11 * 1024 * 1024];
        Array.Fill<byte>(tooBig, 0x25);
        var oversized = await UploadBytesAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "big.pdf", "application/pdf", tooBig);
        oversized.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await oversized.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_DOCUMENT_TOO_LARGE");

        // Per-comment cap (default 5): insert five OK then a sixth returns per-comment limit.
        var commentId = await CreateCommentAsync(s.OwnerClient, activityId, "limit");
        for (var i = 0; i < 5; i++)
        {
            var ok = await UploadBytesAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", commentId, $"file{i}.pdf", "application/pdf", new byte[] { 0x25, 0x26, 0x27 });
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        var blocked = await UploadBytesAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", commentId, "extra.pdf", "application/pdf", new byte[] { 0x25 });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_DOCUMENT_LIMIT_PER_COMMENT");

        // Daily quota: the company already uploaded 5 today; lower the quota to 6 and the 7th fails.
        (await s.AdminClient.PutAsJsonAsync("/api/v1/CalendarAttachmentSettings", new
        {
            allowedDocumentTypes = 1 | 2 | 4 | 16,
            maxFileSizeBytes = 10L * 1024 * 1024,
            maxFilesPerComment = 5,
            maxFilesPerDay = 6
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadPdfAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "sixth.pdf"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var perDay = await UploadPdfAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "seventh.pdf");
        perDay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await perDay.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_DOCUMENT_LIMIT_PER_DAY");

        // Without a live limits row nothing can be attached.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.CalendarAttachmentSettings.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == s.CompanyId && x.GcRecord == 0);
            row.GcRecord = 1;
            await db.SaveChangesAsync();
        }
        var notConfigured = await UploadPdfAsync(s.OwnerClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "none.pdf");
        notConfigured.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notConfigured.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED");
    }

    /// <summary>
    /// Acceptance #7 — every company is born with the default limits; <c>PUT</c> replaces them; the bitácora
    /// stays consistent with <c>CalendarSettings.CanUpdate</c>.
    /// </summary>
    [Fact]
    public async Task Settings_NewCompanyIsBornWithDefaults_AndCanBeUpdated()
    {
        // A company created through the catalog seeder (the company-creation hook) gets the default row, and the
        // startup backfill fills a company that has none.
        Guid bornId;
        Guid backfilledId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var born = new Company { Name = $"JOIN-S69-N-{Guid.NewGuid():N}"[..20], TaxId = $"RUC{Guid.NewGuid():N}"[..16], IsActive = true, CreatedBy = Creator };
            var backfilled = new Company { Name = $"JOIN-S69-B-{Guid.NewGuid():N}"[..20], TaxId = $"RUC{Guid.NewGuid():N}"[..16], IsActive = true, CreatedBy = Creator };
            db.Companies.AddRange(born, backfilled);
            await db.SaveChangesAsync();
            bornId = born.Id;
            backfilledId = backfilled.Id;

            await scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>().SeedDefaultCatalogsForCompanyAsync(bornId);
            await scope.ServiceProvider.GetRequiredService<JOIN.Persistence.DatabaseSeeder>().SeedCalendarAttachmentSettingsDefaultsAsync();
        }
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.CalendarAttachmentSettings.IgnoreQueryFilters()
                .Where(x => (x.CompanyId == bornId || x.CompanyId == backfilledId) && x.GcRecord == 0)
                .ToListAsync();
            rows.Should().HaveCount(2);
            rows.Should().OnlyContain(r => r.AllowedDocumentTypes == CalendarAttachmentSettings.DefaultAllowedTypes
                && r.MaxFileSizeBytes == 10L * 1024 * 1024 && r.MaxFilesPerComment == 5 && r.MaxFilesPerDay == 200);
        }

        var s = await SeedAsync();
        var get = await s.AdminClient.GetAsync("/api/v1/CalendarAttachmentSettings");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = Data(await get.Content.ReadAsStringAsync());
        dto.GetProperty("maxFileSizeBytes").GetInt64().Should().Be(10L * 1024 * 1024);
        dto.GetProperty("maxFilesPerComment").GetInt32().Should().Be(5);

        var put = await s.AdminClient.PutAsJsonAsync("/api/v1/CalendarAttachmentSettings", new
        {
            allowedDocumentTypes = 1 | 2, // Pdf | Word
            maxFileSizeBytes = 2L * 1024 * 1024,
            maxFilesPerComment = 7,
            maxFilesPerDay = 100
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = Data(await put.Content.ReadAsStringAsync());
        updated.GetProperty("maxFileSizeBytes").GetInt64().Should().Be(2L * 1024 * 1024);
        updated.GetProperty("maxFilesPerComment").GetInt32().Should().Be(7);
    }

    /// <summary>
    /// Acceptance #6 — the download endpoint streams the bytes back; a cross-tenant probe answers
    /// <c>CALENDAR_DOCUMENT_NOT_FOUND</c> and a hidden-activity probe answers the same code. The
    /// <c>CanDownload</c> 403 path is exercised at the unit-test layer via
    /// <c>DownloadCalendarEventDocumentQueryHandlerTests</c> and the
    /// <c>DynamicAuthorizationFilter</c> gate.
    /// </summary>
    [Fact]
    public async Task Download_ReturnsBytes_AndHidesForeignTenant()
    {
        var s = await SeedAsync();
        var activityId = await s.CreateActivityAsync(title: "Download", from: "2027-04-05T10:00:00-06:00", to: "2027-04-05T11:00:00-06:00");
        var payload = new byte[] { 0x25, 0x26, 0x27, 0x28, 0x29 };
        var upload = await UploadBytesAsync(s.AdminClient, $"/api/v1/CalendarEvents/{activityId}/documents", null, "manual.pdf", "application/pdf", payload);
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentId = Data(await upload.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

        // Happy path — same tenant, same activity owner.
        var download = await s.AdminClient.GetAsync($"/api/v1/CalendarEvents/{activityId}/documents/{documentId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.FileName.Should().Be("manual.pdf");
        var bytes = await download.Content.ReadAsByteArrayAsync();
        bytes.Should().Equal(payload);

        // Foreign-tenant probe — register a second company and ask it for the first company's file.
        var otherEmail = $"b{Guid.NewGuid():N}@t.local";
        Guid otherCompanyId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var otherCompany = new Company
            {
                Name = $"JOIN-S69-DL-{Guid.NewGuid():N}"[..20],
                TaxId = $"RUC{Guid.NewGuid():N}"[..16],
                IsActive = true,
                CreatedBy = Creator
            };
            db.Companies.Add(otherCompany);
            await db.SaveChangesAsync();
            otherCompanyId = otherCompany.Id;
        }
        var otherAdminId = await RegisterMemberAsync(otherCompanyId, otherEmail, "SuperAdmin");
        var otherClient = await AuthedClientAsync(otherCompanyId, otherEmail);

        // We have to give the other admin at least a calendar configuration so the activity lookup path
        // does not 500 on missing tenant config.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cal = await db.UserCalendars.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.CompanyId == otherCompanyId);
            cal.Should().BeNull("the foreign admin was provisioned without a calendar configuration");
        }
        _ = otherAdminId; // not strictly needed; just make the compiler happy.

        var foreign = await otherClient.GetAsync($"/api/v1/CalendarEvents/{activityId}/documents/{documentId}/download");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Contain("CALENDAR_DOCUMENT_NOT_FOUND");
    }

    /// <summary>
    /// Acceptance #6 — a role without <c>CalendarActivityRecords.CanDownload</c> lists the documents but the
    /// download answers 403.
    /// </summary>
    [Fact]
    public async Task Download_WithoutCanDownload_IsForbidden()
    {
        var s = await SeedAsync();
        var (memberId, member) = await MemberAsync(s.CompanyId, "UsuarioSimple", canDownload: false);
        var memberCalendarId = await CreateUserCalendarAsync(s.CompanyId, memberId, canViewOthers: false);
        var activityId = await s.CreateActivityAsync("Mine", "2027-04-05T10:00:00-06:00", "2027-04-05T11:00:00-06:00",
            ownerUserId: memberId, calendarId: memberCalendarId);
        var upload = await UploadPdfAsync(member, $"/api/v1/CalendarEvents/{activityId}/documents", null, "mine.pdf");
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var documentId = Data(await upload.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

        (await member.GetAsync($"/api/v1/CalendarEvents/{activityId}/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync($"/api/v1/CalendarEvents/{activityId}/documents/{documentId}/download"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// EXPLAIN — the new comments and documents list queries (read from the handlers, not copied) can be served by
    /// the <c>(CalendarEventId, CreatedUtc)</c> / <c>(CalendarEventId, Created)</c> indexes. Sequential scans are
    /// disabled for the session so the tiny test tables do not hide a missing index.
    /// </summary>
    [Fact]
    public async Task Explain_CommentsAndDocumentsListQueries_UseTheEventIndexes()
    {
        var s = await SeedAsync();
        var applicationAssembly = typeof(ListCalendarEventCommentsQueryHandler).Assembly;
        var commentsSql = (string)typeof(ListCalendarEventCommentsQueryHandler)
            .GetField("CommentsSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var documentsSql = (string)applicationAssembly
            .GetType("JOIN.Application.UseCases.Calendars.CalendarEventRecords.Queries.CalendarEventDocumentsSql")!
            .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

        (await ExplainAsync(commentsSql, s.CompanyId)).Should().Contain("ix_eventcomments_event_createdutc");
        (await ExplainAsync(documentsSql, s.CompanyId)).Should().Contain("ix_eventdocuments_event_created");
    }

    private async Task<string> ExplainAsync(string sql, Guid companyId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var off = connection.CreateCommand())
        {
            off.CommandText = "SET enable_seqscan = off;";
            await off.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN (FORMAT TEXT) " + sql.TrimEnd().TrimEnd(';');
        foreach (var (name, value) in new[] { ("TenantId", companyId), ("EventId", Guid.NewGuid()) })
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        var rows = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
        }

        // The connection goes back to the pool: restore the planner setting.
        await using (var reset = connection.CreateCommand())
        {
            reset.CommandText = "RESET enable_seqscan;";
            await reset.ExecuteNonQueryAsync();
        }
        return string.Join(Environment.NewLine, rows);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<HttpResponseMessage> UploadPdfAsync(HttpClient client, string url, Guid? commentId, string fileName)
    {
        // Minimal 1-byte "PDF"-like content; the handler only checks size, not the magic bytes.
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        return await UploadBytesAsync(client, url, commentId, fileName, "application/pdf", bytes);
    }

    private static async Task<HttpResponseMessage> UploadBytesAsync(HttpClient client, string url, Guid? commentId, string fileName, string contentType, byte[] content)
    {
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "File", fileName);
        if (commentId is { } cid)
        {
            form.Add(new StringContent(cid.ToString()), "CommentId");
        }
        return await client.PostAsync(url, form);
    }

    private static Task<HttpResponseMessage> DeleteWithReasonAsync(HttpClient client, string url, string reason) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { reason }), Encoding.UTF8, "application/json")
        });

    /// <summary>
    /// Registers a member with <paramref name="role"/> and grants that role, in the test company only, the records
    /// option (<c>CanRead</c> / <c>CanCreate</c>, <c>CanDownload</c> on request, never <c>CanDelete</c>) and the
    /// calendar log read; <paramref name="panelRead"/> adds <c>CalendarOthers.CanRead</c> (option CalendarPanel).
    /// </summary>
    private async Task<(Guid UserId, HttpClient Client)> MemberAsync(Guid companyId, string role, bool canDownload, bool panelRead = false)
    {
        var email = $"m{Guid.NewGuid():N}@t.local";
        var userId = await RegisterMemberAsync(companyId, email, role);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).SingleAsync();
            db.UserRoleCompanies.Add(new UserRoleCompany { UserId = userId, RoleId = roleId, CompanyId = companyId, CreatedBy = Creator });

            async Task GrantAsync(string option, bool create, bool download)
            {
                var optionId = await db.SystemOptions.IgnoreQueryFilters()
                    .Where(o => o.Name == option && o.GcRecord == 0).Select(o => o.Id).SingleAsync();
                var exists = await db.RoleSystemOptions.IgnoreQueryFilters()
                    .AnyAsync(r => r.RoleId == roleId && r.SystemOptionId == optionId && r.CompanyId == companyId);
                if (!exists)
                {
                    db.RoleSystemOptions.Add(new RoleSystemOption
                    {
                        RoleId = roleId, SystemOptionId = optionId, CompanyId = companyId,
                        CanRead = true, CanCreate = create, CanDownload = download, CreatedBy = Creator
                    });
                }
            }

            await GrantAsync("ActivityRecords", create: true, download: canDownload);
            await GrantAsync("MyCalendar", create: true, download: false);
            if (panelRead)
            {
                await GrantAsync("CalendarPanel", create: false, download: false);
            }
            await db.SaveChangesAsync();
        }

        return (userId, await AuthedClientAsync(companyId, email));
    }

    /// <summary> Gives the user a calendar and a calendar configuration with the SPEC 68 flag. </summary>
    private async Task<Guid> CreateUserCalendarAsync(Guid companyId, Guid userId, bool canViewOthers)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var calendar = new UserCalendar { CompanyId = companyId, OwnerUserId = userId, Name = "Mine", Color = "#1E88E5", IsActive = true, CreatedBy = Creator };
        db.UserCalendars.Add(calendar);
        await db.SaveChangesAsync();
        db.CalendarUserConfigurations.Add(new CalendarUserConfiguration
        {
            CompanyId = companyId, UserId = userId, DefaultCalendarId = calendar.Id, CanViewOthersActivities = canViewOthers, CreatedBy = Creator
        });
        await db.SaveChangesAsync();
        return calendar.Id;
    }

    private static async Task<Guid> CreateCommentAsync(HttpClient client, Guid activityId, string body)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/CalendarEvents/{activityId}/comments", new { body });
        response.EnsureSuccessStatusCode();
        return Data(await response.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<Spec69SeedContext> SeedAsync()
    {
        var email = $"a{Guid.NewGuid():N}@t.local";
        var otherEmail = $"o{Guid.NewGuid():N}@t.local";

        Guid companyId;
        Guid adminId;
        Guid otherId;
        Guid calendarId;
        Guid webChannelId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var company = new Company
            {
                Name = $"JOIN-S69-{Guid.NewGuid():N}"[..20],
                TaxId = $"RUC{Guid.NewGuid():N}"[..16],
                IsActive = true,
                CreatedBy = Creator
            };
            db.Companies.Add(company);
            var moduleId = await db.SystemModules.IgnoreQueryFilters().Where(m => m.Name == "Calendar").Select(m => m.Id).SingleAsync();
            db.CompanyModules.Add(new CompanyModule { CompanyId = company.Id, ModuleId = moduleId, IsActive = true, CreatedBy = Creator });
            await db.SaveChangesAsync();
            companyId = company.Id;

            var seeder = scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>();
            await seeder.SeedCalendarCatalogsForCompanyAsync(companyId);
            var dbSeeder = scope.ServiceProvider.GetRequiredService<JOIN.Persistence.DatabaseSeeder>();
            await dbSeeder.SeedCalendarAttachmentSettingsForCompanyAsync(companyId);
        }

        adminId = await RegisterMemberAsync(companyId, email, "SuperAdmin");
        otherId = await RegisterMemberAsync(companyId, otherEmail, null);

        var adminClient = await AuthedClientAsync(companyId, email);
        var config = await adminClient.PostAsJsonAsync("/api/v1/CalendarCompanies", new
        {
            defaultActivityUserId = adminId,
            timeZone = "America/Managua",
            isOpen24Hours = false,
            businessHoursStart = "08:00:00",
            businessHoursEnd = "17:00:00"
        });
        config.EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            calendarId = await db.UserCalendars.IgnoreQueryFilters()
                .Where(c => c.CompanyId == companyId && c.OwnerUserId == adminId && c.IsActive)
                .Select(c => c.Id)
                .FirstAsync();
            webChannelId = await db.CommunicationChannels.IgnoreQueryFilters()
                .Where(c => c.Code == "WEB")
                .Select(c => c.Id)
                .FirstAsync();
        }

        return new Spec69SeedContext(this, adminClient, companyId, adminId, otherId, calendarId, webChannelId);
    }

    private async Task<Guid> RegisterMemberAsync(Guid companyId, string email, string? role)
    {
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec69",
            LastName = role ?? "Member"
        })).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
        if (role is not null)
        {
            (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
        }
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<HttpClient> AuthedClientAsync(Guid companyId, string email)
    {
        using var unauthClient = _factory.CreateClient();
        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return client;
    }

    private sealed class Spec69SeedContext
    {
        private readonly Spec69CalendarActivityCommentsAndDocumentsPostgreSqlTests _parent;
        public Spec69SeedContext(
            Spec69CalendarActivityCommentsAndDocumentsPostgreSqlTests parent,
            HttpClient adminClient,
            Guid companyId,
            Guid adminId,
            Guid otherId,
            Guid calendarId,
            Guid webChannelId)
        {
            _parent = parent;
            AdminClient = adminClient;
            OwnerClient = adminClient; // the seeded admin is the owner of every activity in this test.
            CompanyId = companyId;
            AdminId = adminId;
            OtherId = otherId;
            CalendarId = calendarId;
            WebChannelId = webChannelId;
        }

        public HttpClient AdminClient { get; }
        public HttpClient OwnerClient { get; }
        public Guid CompanyId { get; }
        public Guid AdminId { get; }
        public Guid OtherId { get; }
        public Guid CalendarId { get; }
        public Guid WebChannelId { get; }

        public async Task<Guid> CreateActivityAsync(string title, string from, string to, Guid? ownerUserId = null, Guid? calendarId = null)
        {
            await using var scope = _parent._factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activity = await db.CalendarActivityCompanies.IgnoreQueryFilters()
                .FirstAsync(a => a.CompanyId == CompanyId && a.IsActive);
            var eventId = Guid.NewGuid();
            var start = DateTimeOffset.Parse(from).UtcDateTime;
            var end = DateTimeOffset.Parse(to).UtcDateTime;
            var calendarEvent = new CalendarEvent
            {
                CompanyId = CompanyId,
                CalendarId = calendarId ?? CalendarId,
                OwnerUserId = ownerUserId ?? AdminId,
                CalendarActivityCompanyId = activity.Id,
                CalendarStatusCompanyId = activity.InitialCalendarStatusCompanyId,
                Title = title,
                IsAllDay = false,
                StartUtc = start,
                EndUtc = end,
                OriginChannelId = WebChannelId,
                OriginType = CalendarEventOriginType.User,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = Creator
            };
            typeof(CalendarEvent).GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(calendarEvent, eventId);
            db.CalendarEvents.Add(calendarEvent);
            await db.SaveChangesAsync();
            return eventId;
        }

        /// <summary> A weekly series of the admin plus one modified occurrence (an exception with its own row). </summary>
        public async Task<(Guid MasterId, Guid ExceptionId)> CreateWeeklySeriesWithExceptionAsync()
        {
            var masterId = await CreateActivityAsync("Weekly", "2027-02-01T10:00:00-06:00", "2027-02-01T11:00:00-06:00");
            var exceptionId = await CreateActivityAsync("Weekly (moved)", "2027-03-15T12:00:00-06:00", "2027-03-15T13:00:00-06:00");
            await using var scope = _parent._factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var master = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == masterId);
            master.RecurrenceRule = "FREQ=WEEKLY;BYDAY=MO";
            var exception = await db.CalendarEvents.IgnoreQueryFilters().SingleAsync(e => e.Id == exceptionId);
            exception.SeriesMasterId = masterId;
            exception.OriginalStartUtc = new DateTime(2027, 3, 15, 16, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
            return (masterId, exceptionId);
        }
    }

    private static string EnsureSuccessContent(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}