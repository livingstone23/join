// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Messaging;

/// <summary>
/// SPEC 52 — inbox summary endpoint (<c>GET /api/v1/Tickets/summary</c>) and
/// <c>?unassigned=true</c> filter on <c>GET /api/v1/Tickets</c>. Covers the four
/// counts for the active tenant, soft-delete / final-status exclusions, the
/// SLA-breached and inactive parity with the grid, and the cross-filter
/// validation against <c>assignedToUserId</c>.
/// </summary>
/// <remarks>
/// Tickets are seeded via raw SQL inserts that bypass
/// <c>AuditableEntitySaveChangesInterceptor</c> — the interceptor overwrites
/// <c>Created</c> on every <c>Added</c> entity with <c>DateTime.UtcNow</c>, so
/// any backdated ticket the test wants to use for the SLA / inactivity
/// calculator would land in the DB at "now" and never breach.
/// </remarks>
public sealed class TicketInboxSummaryPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private const string Creator = "spec52-tests";

    private readonly CustomWebApplicationFactory _factory;

    public TicketInboxSummaryPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Happy path: with a mix of assigned, unassigned, breached and inactive
    /// open tickets, plus a final-status one that closed far past its SLA, the
    /// summary returns the right four counts. The final ticket stays active
    /// (<c>GcRecord = 0</c>) so its exclusion comes from <c>IsFinal</c> alone; a
    /// soft-deleted open ticket and the tickets of another company are excluded too.
    /// </summary>
    [Fact]
    public async Task Summary_ShouldReturnTheFourCountsAndExcludeFinalAndSoftDeletedTickets()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: true);
        await SeedTicketsAsync(seeded);

        // Soft-deleted open, unassigned, breached ticket: must not count anywhere.
        var deletedOpenId = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_factory.ConnectionString))
        {
            await conn.OpenAsync();
            await InsertTicketAsync(conn, seeded, deletedOpenId, DateTime.UtcNow.AddDays(-20), seeded.InProgressStatusId, null);
        }
        await SoftDeleteTicketAsync(deletedOpenId);

        // Another company with the same fixture: its tickets must not leak into this tenant's counts.
        var otherTenant = await SeedTenantAsync(configureInactivityThreshold: true);
        await SeedTicketsAsync(otherTenant);

        using var client = await CreateClientWithTicketsPermissionAsync(seeded.CompanyId);
        var response = await client.GetAsync("/api/v1/Tickets/summary");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var data = Data(body);
        // The seeded fixture (all 1h SLA × 1h TimeUnit → very tight):
        //   openAssigned      → created -5m      (not breached, not inactive)
        //   openUnassigned    → created -200h    (breached, inactive — >7d)
        //   breachedAssigned  → created -72h     (breached, not inactive)
        //   inactive          → created -30d     (breached, inactive)
        //   finished          → IsFinal, closed 90 days late, still active → excluded by IsFinal
        //   deletedOpen       → open + unassigned + breached, soft-deleted → excluded
        //   other company     → same five tickets in another tenant → excluded
        data.GetProperty("openCount").GetInt32().Should().Be(4);
        data.GetProperty("slaBreachedCount").GetInt32().Should().Be(3);
        data.GetProperty("inactiveCount").GetInt32().Should().Be(2);
        data.GetProperty("unassignedCount").GetInt32().Should().Be(1);
        data.GetProperty("generatedAt").GetDateTime().Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    /// <summary>
    /// The summary and the grid must agree on breach/inactivity for the same
    /// set of open tickets (acceptance criterion #3 of SPEC 52).
    /// </summary>
    [Fact]
    public async Task Summary_SlaAndInactiveCounts_ShouldMatchTheGridMarkers()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: true);
        await SeedTicketsAsync(seeded);

        using var client = await CreateClientWithTicketsPermissionAsync(seeded.CompanyId);

        var summaryResponse = await client.GetAsync("/api/v1/Tickets/summary");
        var summaryBody = await summaryResponse.Content.ReadAsStringAsync();
        summaryResponse.StatusCode.Should().Be(HttpStatusCode.OK, summaryBody);
        var summary = Data(summaryBody);

        // Filter the grid to open tickets so the sets are the same — the
        // summary excludes IsFinal, the grid does not.
        var expectedSla = 0;
        var expectedInactive = 0;
        var expectedOpen = 0;
        var pageSize = 50;
        for (var page = 1; ; page++)
        {
            var listResponse = await client.GetAsync(
                $"/api/v1/Tickets?pageNumber={page}&pageSize={pageSize}&ticketStatusId={seeded.InProgressStatusId}");
            var listBody = await listResponse.Content.ReadAsStringAsync();
            listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
            var listData = Data(listBody);
            var items = listData.GetProperty("items").EnumerateArray().ToList();
            expectedOpen += items.Count;
            foreach (var item in items)
            {
                if (item.GetProperty("isSlaBreached").GetBoolean()) expectedSla++;
                if (item.GetProperty("isInactive").GetBoolean()) expectedInactive++;
            }
            if (items.Count < pageSize) break;
        }

        summary.GetProperty("openCount").GetInt32().Should().Be(expectedOpen);
        summary.GetProperty("slaBreachedCount").GetInt32().Should().Be(expectedSla);
        summary.GetProperty("inactiveCount").GetInt32().Should().Be(expectedInactive);
    }

    /// <summary>
    /// A tenant whose <c>TicketCompanyDefaults</c> row has no
    /// <c>MaxDayTicketInactivity</c> cannot have any inactive tickets — the
    /// calculator returns <c>IsInactive = false</c> for every open row.
    /// </summary>
    [Fact]
    public async Task Summary_WhenCompanyHasNoInactivityThreshold_ShouldReportZeroInactive()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: false);
        // Insert a one-year-old open ticket via raw SQL so the Created value
        // survives the save interceptor.
        await using (var conn = new NpgsqlConnection(_factory.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO messaging.tickets
                    ("id", "companyid", "code", "name", "description",
                     "estimatedtime", "consumedtime", "isvisibletoexternals",
                     "timeunitid", "ticketstatusid", "ticketcomplexityid",
                     "channelid", "createdbyuserid", "gcrecord",
                     "created", "createdby")
                VALUES (@id, @company, @code, @name, @desc,
                        1, 0, FALSE,
                        @timeUnit, @status, @complexity,
                        @channel, @user, 0,
                        @created, @creator)
                """, conn);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("company", seeded.CompanyId);
            cmd.Parameters.AddWithValue("code", $"S52-{Guid.NewGuid():N}"[..14].ToUpperInvariant());
            cmd.Parameters.AddWithValue("name", "Old open ticket");
            cmd.Parameters.AddWithValue("desc", "Should never show as inactive.");
            cmd.Parameters.AddWithValue("timeUnit", seeded.TimeUnitId);
            cmd.Parameters.AddWithValue("status", seeded.InitialStatusId);
            cmd.Parameters.AddWithValue("complexity", seeded.ComplexityId);
            cmd.Parameters.AddWithValue("channel", seeded.ChannelId);
            cmd.Parameters.AddWithValue("user", seeded.ManagerUserId);
            cmd.Parameters.AddWithValue("created", DateTime.UtcNow.AddYears(-1));
            cmd.Parameters.AddWithValue("creator", Creator);
            await cmd.ExecuteNonQueryAsync();
        }

        using var client = await CreateClientWithTicketsPermissionAsync(seeded.CompanyId);
        var response = await client.GetAsync("/api/v1/Tickets/summary");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        Data(body).GetProperty("inactiveCount").GetInt32().Should().Be(0);
        Data(body).GetProperty("openCount").GetInt32().Should().Be(1);
    }

    /// <summary>
    /// The summary endpoint shares the permission resource of the listing
    /// endpoint — a caller without <c>CanRead</c> on <c>Tickets</c> must be
    /// rejected with 403, not 401 (SPEC 17 RFC 7235 semantics).
    /// </summary>
    [Fact]
    public async Task Summary_WithoutCanRead_ShouldReturn403()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: false);
        using var client = await CreateClientWithoutTicketsPermissionAsync(seeded.CompanyId);

        var response = await client.GetAsync("/api/v1/Tickets/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// <c>?unassigned=true</c> is the server-side replacement for the page-side
    /// filter the front used to apply. <c>TotalCount</c> matches the items.
    /// </summary>
    [Fact]
    public async Task TicketsPaged_WithUnassignedTrue_ShouldReturnOnlyUnassignedAndAgreeOnTotalCount()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: true);
        var tickets = await SeedTicketsAsync(seeded);

        using var client = await CreateClientWithTicketsPermissionAsync(seeded.CompanyId);

        var response = await client.GetAsync("/api/v1/Tickets?unassigned=true");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var data = Data(body);
        var items = data.GetProperty("items").EnumerateArray().ToList();
        // The fixture has exactly one unassigned ticket (openUnassigned); the other four are assigned.
        items.Should().ContainSingle()
            .Which.GetProperty("id").GetGuid().Should().Be(tickets.OpenUnassigned);
        data.GetProperty("totalCount").GetInt32().Should().Be(1, "the COUNT shares the WHERE clause with the page");
    }

    /// <summary>
    /// <c>?unassigned=true</c> + <c>assignedToUserId</c> is contradictory — the
    /// handler rejects it with 400 <c>INVALID_ASSIGNEE_FILTER</c>.
    /// </summary>
    [Fact]
    public async Task TicketsPaged_WithUnassignedAndAssignedToUserId_ShouldReturnInvalidAssigneeFilter()
    {
        var seeded = await SeedTenantAsync(configureInactivityThreshold: false);
        using var client = await CreateClientWithTicketsPermissionAsync(seeded.CompanyId);

        var response = await client.GetAsync(
            $"/api/v1/Tickets?unassigned=true&assignedToUserId={Guid.NewGuid()}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("INVALID_ASSIGNEE_FILTER");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private sealed record SeededTenant(
        Guid CompanyId,
        Guid ManagerUserId,
        Guid InitialStatusId,
        Guid InProgressStatusId,
        Guid FinalStatusId,
        Guid ComplexityId,
        Guid TimeUnitId,
        Guid ChannelId);

    private sealed record SeededTickets(Guid OpenAssigned, Guid OpenUnassigned, Guid BreachedAssigned, Guid Inactive, Guid Finished);

    private async Task<SeededTenant> SeedTenantAsync(bool configureInactivityThreshold)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var company = new Company
        {
            Name = $"JOIN-S52-{Guid.NewGuid():N}"[..20],
            TaxId = $"RUC{Guid.NewGuid():N}"[..16],
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = Creator
        };
        db.Companies.Add(company);

        TicketStatus Status(string name, bool isInitial = false, bool isFinal = false) => new()
        {
            CompanyId = company.Id,
            Name = $"{name}-{Guid.NewGuid():N}"[..20],
            Code = Random.Shared.Next(1000, 999_999),
            IsActive = true,
            IsInitial = isInitial,
            IsFinal = isFinal,
            CreatedBy = Creator
        };

        var initial = Status("Open", isInitial: true);
        var inProgress = Status("Progress");
        var final = Status("Closed", isFinal: true);
        var timeUnit = new TimeUnit
        {
            CompanyId = company.Id,
            Name = $"U{Guid.NewGuid():N}"[..20],
            Code = 1, // hours; ResolutionTimeUnits = 1 → very tight 1h SLA.
            CreatedBy = Creator
        };
        var complexity = new TicketComplexity
        {
            CompanyId = company.Id,
            Name = $"X{Guid.NewGuid():N}"[..20],
            Code = Random.Shared.Next(1000, 999_999),
            ResolutionTimeUnits = 1,
            TimeUnitId = timeUnit.Id,
            CreatedBy = Creator
        };
        var channel = new CommunicationChannel
        {
            Name = $"CH{Guid.NewGuid():N}"[..20],
            IsActive = true,
            CreatedBy = Creator
        };
        db.AddRange(initial, inProgress, final, timeUnit, complexity, channel);

        var managerEmail = $"mgr{Guid.NewGuid():N}@t.local";
        var user = new ApplicationUser
        {
            Email = managerEmail,
            UserName = managerEmail,
            FirstName = "Spec52",
            LastName = "Manager",
            EmailConfirmed = true,
            CreatedBy = Creator
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await db.SaveChangesAsync();

        db.UserCompanies.Add(new UserCompany
        {
            UserId = user.Id,
            CompanyId = company.Id,
            IsDefault = true,
            CreatedBy = Creator
        });
        db.TicketCompanyDefaults.Add(new TicketCompanyDefault
        {
            CompanyId = company.Id,
            StartCode = "TCK",
            CodeSequenceLength = 6,
            TicketStatusDefaultId = initial.Id,
            MaxDayTicketInactivity = configureInactivityThreshold ? 7 : null,
            CreatedBy = Creator
        });
        await db.SaveChangesAsync();

        return new SeededTenant(
            company.Id, user.Id, initial.Id, inProgress.Id, final.Id,
            complexity.Id, timeUnit.Id, channel.Id);
    }

    /// <summary>
    /// Inserts the five backdated tickets via raw SQL — the
    /// AuditableEntitySaveChangesInterceptor overwrites <c>Created</c> with
    /// <c>DateTime.UtcNow</c> on every EF <c>Added</c> entity, so backdated
    /// tickets must bypass EF for the Created column.
    /// </summary>
    private async Task<SeededTickets> SeedTicketsAsync(SeededTenant seeded)
    {
        var openAssignedId = Guid.NewGuid();
        var openUnassignedId = Guid.NewGuid();
        var breachedAssignedId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var finishedId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_factory.ConnectionString);
        await conn.OpenAsync();
        var now = DateTime.UtcNow;
        // "Recent" ticket must be recent enough that SLA is not breached by the time the
        // handler executes — the seed + endpoint roundtrip takes a few seconds.
        await InsertTicketAsync(conn, seeded, openAssignedId, now.AddMinutes(-5), seeded.InProgressStatusId, seeded.ManagerUserId);
        await InsertTicketAsync(conn, seeded, openUnassignedId, now.AddHours(-200), seeded.InProgressStatusId, null);
        await InsertTicketAsync(conn, seeded, breachedAssignedId, now.AddHours(-72), seeded.InProgressStatusId, seeded.ManagerUserId);
        await InsertTicketAsync(conn, seeded, inactiveId, now.AddDays(-30), seeded.InProgressStatusId, seeded.ManagerUserId);
        await InsertTicketAsync(conn, seeded, finishedId, now.AddDays(-90), seeded.FinalStatusId, seeded.ManagerUserId);

        return new SeededTickets(openAssignedId, openUnassignedId, breachedAssignedId, inactiveId, finishedId);
    }

    private static async Task InsertTicketAsync(
        NpgsqlConnection conn,
        SeededTenant seeded,
        Guid id,
        DateTime created,
        Guid statusId,
        Guid? assigneeId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO messaging.tickets
                ("id", "companyid", "code", "name", "description",
                 "estimatedtime", "consumedtime", "isvisibletoexternals",
                 "timeunitid", "ticketstatusid", "ticketcomplexityid",
                 "channelid", "createdbyuserid", "assignedtouserid", "gcrecord",
                 "created", "createdby")
            VALUES (@id, @company, @code, @name, @desc,
                    1, 0, FALSE,
                    @timeUnit, @status, @complexity,
                    @channel, @user, @assignee, 0,
                    @created, @creator)
            """, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("company", seeded.CompanyId);
        cmd.Parameters.AddWithValue("code", $"S52-{id:N}"[..14].ToUpperInvariant());
        cmd.Parameters.AddWithValue("name", $"Spec52 {id:N}"[..10]);
        cmd.Parameters.AddWithValue("desc", "Spec52 seed");
        cmd.Parameters.AddWithValue("timeUnit", seeded.TimeUnitId);
        cmd.Parameters.AddWithValue("status", statusId);
        cmd.Parameters.AddWithValue("complexity", seeded.ComplexityId);
        cmd.Parameters.AddWithValue("channel", seeded.ChannelId);
        cmd.Parameters.AddWithValue("user", seeded.ManagerUserId);
        cmd.Parameters.AddWithValue("assignee", (object?)assigneeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("created", created);
        cmd.Parameters.AddWithValue("creator", Creator);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task SoftDeleteTicketAsync(Guid ticketId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ticket = await db.Tickets.IgnoreQueryFilters().FirstAsync(t => t.Id == ticketId);
        ticket.MarkAsDeleted();
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a fresh SuperAdmin user bound to the test tenant. SuperAdmin
    /// bypasses <c>DynamicAuthorizationFilter</c> (SPEC 17), so the test does
    /// not need to seed <c>RoleSystemOptions</c> for the new company — the
    /// handler still applies the tenant filter via <c>CompanyId</c> from the JWT.
    /// </summary>
    private async Task<HttpClient> CreateClientWithTicketsPermissionAsync(Guid companyId)
    {
        var email = $"mgr{Guid.NewGuid():N}@t.local";
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec52",
            LastName = "Manager"
        })).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            db.UserCompanies.Add(new UserCompany
            {
                UserId = user!.Id,
                CompanyId = companyId,
                IsDefault = true,
                CreatedBy = Creator
            });
            (await userManager.AddToRoleAsync(user, "SuperAdmin")).Succeeded.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand
        {
            Email = email,
            Password = Password,
            TargetCompanyId = companyId
        });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        return client;
    }

    /// <summary>
    /// Creates a fresh user bound to the test tenant with no role assignments.
    /// Without roles, no <c>RoleSystemOption</c> flag resolves to true, so
    /// <c>DynamicAuthorizationFilter</c> returns <c>ForbidResult</c> (403).
    /// </summary>
    private async Task<HttpClient> CreateClientWithoutTicketsPermissionAsync(Guid companyId)
    {
        var email = $"noaccess{Guid.NewGuid():N}@t.local";
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "No",
            LastName = "Access"
        })).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            db.UserCompanies.Add(new UserCompany
            {
                UserId = user!.Id,
                CompanyId = companyId,
                IsDefault = true,
                CreatedBy = Creator
            });
            await db.SaveChangesAsync();
            // No role → 403 by DynamicAuthorizationFilter.
        }

        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand
        {
            Email = email,
            Password = Password,
            TargetCompanyId = companyId
        });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        return client;
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}