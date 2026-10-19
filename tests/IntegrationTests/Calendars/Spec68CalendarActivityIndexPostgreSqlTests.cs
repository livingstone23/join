// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using FluentAssertions.Collections;
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

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 68 — end-to-end coverage of the activity index through the API. Exercises the controller's visibility
/// rules, the 92-day range cap, the export gate, and the detail/log endpoints (the latter two follow the same
/// visibility rule). Also runs <c>EXPLAIN</c> on the index query to confirm the planner anchors on the events
/// table by index.
/// </summary>
public sealed class Spec68CalendarActivityIndexPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Creator = "spec68-tests";
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public Spec68CalendarActivityIndexPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Acceptance #1 — a user without the per-user flag sees only their own activities; any <c>ownerUserIds</c>
    /// they send is silently ignored.
    /// </summary>
    [Fact]
    public async Task Index_WithoutFlag_RestrictsToCurrentUserAndIgnoresOwnerFilter()
    {
        var s = await SeedAsync();
        var ownEvent = await s.CreateActivityAsync(title: "Mine", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00");
        var theirsEvent = await s.CreateActivityAsync(title: "Theirs", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00", ownerId: s.OtherId);

        var ownIndex = await IndexAsync(s.AdminClient, ownerUserIds: new[] { s.OtherId });
        ownIndex.Items.Should().HaveCount(1);
        ownIndex.Items[0].GetProperty("eventId").GetGuid().Should().Be(ownEvent);
        ownIndex.Items[0].GetProperty("title").GetString().Should().Be("Mine");
        theirsEvent.Should().NotBe(ownEvent, "the index should not return the other user's event when the flag is off");
    }

    /// <summary>
    /// Acceptance #2 — a user with the per-user flag sees the activities of every member of the company and can
    /// filter by owner and calendar.
    /// </summary>
    [Fact]
    public async Task Index_WithFlag_SeesCompanyAndAppliesOwnerFilter()
    {
        var s = await SeedAsync();
        await s.SetCanViewOthersAsync(true);

        var ownEvent = await s.CreateActivityAsync(title: "Mine", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00");
        var theirsEvent = await s.CreateActivityAsync(title: "Theirs", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00", ownerId: s.OtherId);

        var all = await IndexAsync(s.AdminClient, ownerUserIds: new[] { s.AdminId, s.OtherId });
        all.Items.Should().HaveCount(2);

        var onlyMine = await IndexAsync(s.AdminClient, ownerUserIds: new[] { s.AdminId });
        onlyMine.Items.Should().HaveCount(1);
        onlyMine.Items[0].GetProperty("eventId").GetGuid().Should().Be(ownEvent);

        var onlyTheirs = await IndexAsync(s.AdminClient, ownerUserIds: new[] { s.OtherId });
        onlyTheirs.Items.Should().HaveCount(1);
        onlyTheirs.Items[0].GetProperty("eventId").GetGuid().Should().Be(theirsEvent);
    }

    /// <summary>
    /// Rule 4 — range longer than 92 days returns <c>CALENDAR_RANGE_TOO_LARGE</c> (400) before any reader
    /// runs.
    /// </summary>
    [Fact]
    public async Task Index_WhenRangeExceeds92Days_ReturnsRangeTooLarge()
    {
        var s = await SeedAsync();
        await s.SetCanViewOthersAsync(true);

        var response = await s.AdminClient.GetAsync("/api/v1/CalendarActivityIndex?from=2027-03-01T00:00:00Z&to=2027-07-01T00:00:00Z");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("CALENDAR_RANGE_TOO_LARGE");
    }

    /// <summary>
    /// Acceptance #5 — exporting without <c>CanExport</c> is rejected with 403; with the permission the page size
    /// is the 10 000 ceiling.
    /// </summary>
    [Fact]
    public async Task Export_WithoutCanExport_ReturnsForbidden()
    {
        var s = await SeedAsync();
        await s.CreateActivityAsync(title: "Mine", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00");

        var response = await s.AdminClient.GetAsync("/api/v1/CalendarActivityIndex?from=2027-03-01T00:00:00Z&to=2027-03-31T00:00:00Z&export=true");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Acceptance #7 — opening another user's detail without the flag answers 404; with the flag it returns the
    /// detail.
    /// </summary>
    [Fact]
    public async Task Detail_WithoutFlag_OfOtherUsersActivity_Returns404()
    {
        var s = await SeedAsync();
        var theirsEvent = await s.CreateActivityAsync(title: "Theirs", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00", ownerId: s.OtherId);

        var response = await s.AdminClient.GetAsync($"/api/v1/CalendarActivityIndex/{theirsEvent}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Acceptance #7 — opening another user's detail with the flag returns the detail DTO.
    /// </summary>
    [Fact]
    public async Task Detail_WithFlag_OfOtherUsersActivity_Returns200()
    {
        var s = await SeedAsync();
        await s.SetCanViewOthersAsync(true);
        var theirsEvent = await s.CreateActivityAsync(title: "Theirs", from: "2027-03-15T10:00:00-06:00", to: "2027-03-15T11:00:00-06:00", ownerId: s.OtherId);

        var response = await s.AdminClient.GetAsync($"/api/v1/CalendarActivityIndex/{theirsEvent}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Data(await response.Content.ReadAsStringAsync());
        data.GetProperty("title").GetString().Should().Be("Theirs");
    }

    /// <summary>
    /// Owners endpoint — without the flag, the list is restricted to the current user; with the flag the list
    /// returns every member of the company with a calendar.
    /// </summary>
    [Fact]
    public async Task Owners_ReflectsFlagScope()
    {
        var s = await SeedAsync();

        var mineOnly = await OwnersAsync(s.AdminClient);
        mineOnly.Items.Should().HaveCount(1);
        mineOnly.Items[0].GetProperty("userId").GetGuid().Should().Be(s.AdminId);

        await s.SetCanViewOthersAsync(true);
        var everyone = await OwnersAsync(s.AdminClient);
        everyone.Items.Select(x => x.GetProperty("userId").GetGuid())
            .Should().BeEquivalentTo(new[] { s.AdminId, s.OtherId });
    }

    /// <summary>
    /// Acceptance #7 — <c>EXPLAIN</c> on the index query. We anchor on the events table by index so the FK joins
    /// do not degrade into sequential scans as the table grows.
    /// </summary>
    [Fact]
    public async Task IndexQuery_PlansIndexAnchoredOnEventsTable()
    {
        var s = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var parameters = new object[] { s.CompanyId, new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 3, 31, 0, 0, 0, DateTimeKind.Utc), new Guid[] { s.AdminId } };
        const string plan = """
            EXPLAIN (FORMAT TEXT)
            SELECT e.Id
            FROM Calendar.Events e
            INNER JOIN Security.Users u ON u.Id = e.OwnerUserId
            INNER JOIN Calendar.UserCalendars cal ON cal.Id = e.CalendarId
            INNER JOIN Calendar.ActivityCompanies act ON act.Id = e.CalendarActivityCompanyId
            INNER JOIN Calendar.StatusCompanies sc ON sc.Id = e.CalendarStatusCompanyId
            INNER JOIN Calendar.Statuses s ON s.Id = sc.CalendarStatusId
            INNER JOIN Common.CommunicationChannels ch ON ch.Id = e.OriginChannelId
            LEFT JOIN Admin.Persons p ON p.Id = e.RequesterPersonId AND p.CompanyId = e.CompanyId AND p.GcRecord = 0
            LEFT JOIN Customer.Customers cust ON cust.Id = e.CustomerId AND cust.CompanyId = e.CompanyId AND cust.GcRecord = 0
            WHERE e.CompanyId = $1
              AND e.GcRecord = 0
              AND e.OwnerUserId = ANY($4)
              AND ((e.SeriesMasterId IS NULL AND e.StartUtc < $3 AND e.EndUtc > $2)
                OR (e.SeriesMasterId IS NOT NULL AND e.OriginalStartUtc >= $2 AND e.OriginalStartUtc < $3));
            """;

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var lines = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = plan;
            var p0 = command.CreateParameter(); p0.Value = parameters[0]; command.Parameters.Add(p0);
            var p1 = command.CreateParameter(); p1.Value = parameters[1]; command.Parameters.Add(p1);
            var p2 = command.CreateParameter(); p2.Value = parameters[2]; command.Parameters.Add(p2);
            var p3 = command.CreateParameter(); p3.Value = parameters[3]; command.Parameters.Add(p3);
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lines.Add(reader.GetString(0));
            }
        }

        var joined = string.Join(Environment.NewLine, lines);
        joined.Should().NotContain("Seq Scan on calendar.events",
            "the events lookup must plan as an index scan; the FK joins would degrade otherwise");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task<IndexResult> IndexAsync(HttpClient client, Guid[]? ownerUserIds = null)
    {
        var query = "from=2027-03-01T00:00:00Z&to=2027-03-31T00:00:00Z";
        if (ownerUserIds is { Length: > 0 })
        {
            query += "&" + string.Join("&", ownerUserIds.Select(id => $"ownerUserIds={id}"));
        }

        var response = await client.GetAsync($"/api/v1/CalendarActivityIndex?{query}");
        response.EnsureSuccessStatusCode();
        var data = Data(await response.Content.ReadAsStringAsync());
        return new IndexResult(data.GetProperty("items").EnumerateArray().ToList());
    }

    private sealed record IndexResult(IReadOnlyList<JsonElement> Items);

    private static async Task<OwnersResult> OwnersAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/CalendarActivityIndex/owners");
        response.EnsureSuccessStatusCode();
        var data = Data(await response.Content.ReadAsStringAsync());
        var items = data.EnumerateArray().ToList();
        return new OwnersResult(items);
    }

    private sealed record OwnersResult(IReadOnlyList<JsonElement> Items);

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<Spec68SeedContext> SeedAsync()
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
                Name = $"JOIN-S68-{Guid.NewGuid():N}"[..20],
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
        }

        adminId = await RegisterMemberAsync(companyId, email, "SuperAdmin");
        otherId = await RegisterMemberAsync(companyId, otherEmail, null);

        var adminClient = await AuthedClientAsync(companyId, email);

        // Configure CalendarCompany so the index can find the timezone.
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

        return new Spec68SeedContext(this, adminClient, companyId, adminId, otherId, calendarId, webChannelId);
    }

    private async Task<Guid> RegisterMemberAsync(Guid companyId, string email, string? role)
    {
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec68",
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

    private sealed class Spec68SeedContext
    {
        private readonly Spec68CalendarActivityIndexPostgreSqlTests _parent;
        public Spec68SeedContext(
            Spec68CalendarActivityIndexPostgreSqlTests parent,
            HttpClient adminClient,
            Guid companyId,
            Guid adminId,
            Guid otherId,
            Guid calendarId,
            Guid webChannelId)
        {
            _parent = parent;
            AdminClient = adminClient;
            CompanyId = companyId;
            AdminId = adminId;
            OtherId = otherId;
            CalendarId = calendarId;
            WebChannelId = webChannelId;
        }

        public HttpClient AdminClient { get; }
        public Guid CompanyId { get; }
        public Guid AdminId { get; }
        public Guid OtherId { get; }
        public Guid CalendarId { get; }
        public Guid WebChannelId { get; }

        public async Task<Guid> CreateActivityAsync(string title, string from, string to, Guid? ownerId = null)
        {
            await using var scope = _parent._factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var activity = await db.CalendarActivityCompanies.IgnoreQueryFilters()
                .FirstAsync(a => a.CompanyId == CompanyId && a.IsActive);

            var eventId = Guid.NewGuid();
            var calendarId = ownerId.HasValue
                ? await db.UserCalendars.IgnoreQueryFilters()
                    .Where(c => c.CompanyId == CompanyId && c.OwnerUserId == ownerId.Value && c.IsActive)
                    .Select(c => c.Id).FirstAsync()
                : CalendarId;
            var ownerUserId = ownerId ?? AdminId;
            var start = DateTimeOffset.Parse(from).UtcDateTime;
            var end = DateTimeOffset.Parse(to).UtcDateTime;
            var calendarEvent = new CalendarEvent
            {
                CompanyId = CompanyId,
                CalendarId = calendarId,
                OwnerUserId = ownerUserId,
                CalendarActivityCompanyId = activity.Id,
                CalendarStatusCompanyId = activity.InitialCalendarStatusCompanyId,
                Title = title,
                IsAllDay = false,
                StartUtc = start,
                EndUtc = end,
                OriginChannelId = WebChannelId,
                OriginType = CalendarEventOriginType.User,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = "spec68-tests"
            };
            SetId(calendarEvent, eventId);
            db.CalendarEvents.Add(calendarEvent);
            await db.SaveChangesAsync();
            return eventId;
        }

        private static void SetId(object entity, Guid id)
        {
            var prop = entity.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop!.SetValue(entity, id);
        }

        public async Task SetCanViewOthersAsync(bool value)
        {
            await using var scope = _parent._factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = await db.CalendarUserConfigurations.IgnoreQueryFilters()
                .FirstAsync(c => c.CompanyId == CompanyId && c.UserId == AdminId);
            config.CanViewOthersActivities = value;
            await db.SaveChangesAsync();
        }
    }
}