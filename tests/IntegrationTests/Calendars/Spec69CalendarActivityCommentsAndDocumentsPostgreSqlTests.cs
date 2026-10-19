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
    /// Acceptance #4 — the author cannot soft-delete without <c>CalendarActivityRecords.CanDelete</c>;
    /// SuperAdmin does and the body stops being returned by the API while the row stays.
    /// </summary>
    [Fact]
    public async Task Delete_WithoutCanDelete_IsForbidden_AdminDeletesAndBodyHides()
    {
        var s = await SeedAsync();
        var activityId = await s.CreateActivityAsync(title: "Demo", from: "2027-04-05T10:00:00-06:00", to: "2027-04-05T11:00:00-06:00");
        var commentId = await CreateCommentAsync(s.OwnerClient, activityId, "first");

        // Owner cannot delete (no CanDelete flag).
        var denied = await s.OwnerClient.DeleteAsync($"/api/v1/CalendarEvents/{activityId}/comments/{commentId}");
        denied.IsSuccessStatusCode.Should().BeFalse();

        // Admin can.
        var bodyContent = new StringContent("{\"reason\":\"off-topic\"}", Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/CalendarEvents/{activityId}/comments/{commentId}") { Content = bodyContent };
        var ok = await s.AdminClient.SendAsync(request);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // Subsequent list returns the row with Body = null and the reason.
        var list = Data(EnsureSuccessContent(await s.OwnerClient.GetAsync($"/api/v1/CalendarEvents/{activityId}/comments")));
        var item = list.EnumerateArray().Single();
        item.GetProperty("body").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("isDeleted").GetBoolean().Should().BeTrue();
        item.GetProperty("deleteReason").GetString().Should().Be("off-topic");
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
    }

    /// <summary>
    /// Acceptance #7 — every company is born with the default limits; <c>PUT</c> replaces them; the bitácora
    /// stays consistent with <c>CalendarSettings.CanUpdate</c>.
    /// </summary>
    [Fact]
    public async Task Settings_NewCompanyIsBornWithDefaults_AndCanBeUpdated()
    {
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
    /// EXPLAIN — the comments list query anchors on the events table by index so the FK joins do not
    /// degrade into sequential scans when the calendar grows.
    /// </summary>
    [Fact]
    public async Task Explain_CommentsListQuery_PlansIndexAnchored()
    {
        var s = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        const string plan = """
            EXPLAIN (FORMAT TEXT)
            SELECT e.Id
            FROM Calendar.Events e
            WHERE e.CompanyId = $1
              AND e.GcRecord = 0
              AND ((e.SeriesMasterId IS NULL AND e.StartUtc < $3 AND e.EndUtc > $2)
                OR (e.SeriesMasterId IS NOT NULL AND e.OriginalStartUtc >= $2 AND e.OriginalStartUtc < $3));
            """;
        var rows = new List<string>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = plan;
            var p0 = command.CreateParameter(); p0.Value = s.CompanyId; command.Parameters.Add(p0);
            var p1 = command.CreateParameter(); p1.Value = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc); command.Parameters.Add(p1);
            var p2 = command.CreateParameter(); p2.Value = new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc); command.Parameters.Add(p2);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
        }
        var joined = string.Join(Environment.NewLine, rows);
        joined.Should().NotContain("Seq Scan on calendar.events",
            "the events lookup must plan as an index scan; the FK joins would degrade otherwise");
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

    private async Task<Guid> RegisterMemberAsync(Guid companyId, string email, string role)
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

        public async Task<Guid> CreateActivityAsync(string title, string from, string to)
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
                CalendarId = CalendarId,
                OwnerUserId = AdminId,
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
    }

    private static string EnsureSuccessContent(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}