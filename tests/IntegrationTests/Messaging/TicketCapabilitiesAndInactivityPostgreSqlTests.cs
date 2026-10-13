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
/// SPEC 53 — <c>GET /api/v1/TicketUserCompanies/me</c>,
/// <c>GET /api/v1/TicketUserCompanies/assignable</c>, and the new
/// <c>MaxDayTicketInactivity</c> field on <c>TicketCompanyDefaults</c>.
/// </summary>
public sealed class TicketCapabilitiesAndInactivityPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private const string Creator = "spec53-tests";

    private readonly CustomWebApplicationFactory _factory;

    public TicketCapabilitiesAndInactivityPostgreSqlTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ── /me capabilities ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WhenCallerIsOnRoster_ShouldReturnCapabilitiesFromRosterRow()
    {
        var tenant = await SeedTenantAsync();
        // Caller = SuperAdmin user (JWT bypass for DynamicAuthorizationFilter).
        // Add a roster row for that same user — the handler resolves the caller's
        // own capabilities from the roster, so any roster row the SuperAdmin
        // user has on the tenant is what /me surfaces.
        var superAdminClient = await CreateSuperAdminClientAsync(tenant.CompanyId);
        var callerId = await dbUserIdFromClientAsync(superAdminClient);
        await AddRosterRowAsync(tenant.CompanyId, callerId, canResolve: true, canFinish: true, isSuperAdmin: true);

        var response = await superAdminClient.GetAsync("/api/v1/TicketUserCompanies/me");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var data = Data(body);
        data.GetProperty("isMember").GetBoolean().Should().BeTrue();
        data.GetProperty("canResolveTicket").GetBoolean().Should().BeTrue();
        data.GetProperty("canFinishTicket").GetBoolean().Should().BeTrue();
        data.GetProperty("isSuperAdminTicket").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Me_WhenCallerIsNotOnRoster_ShouldReturn200WithAllFalseAndIsMemberFalse()
    {
        var tenant = await SeedTenantAsync();
        // Caller = SuperAdmin bound to the tenant but intentionally not on the roster.
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.GetAsync("/api/v1/TicketUserCompanies/me");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var data = Data(body);
        data.GetProperty("isMember").GetBoolean().Should().BeFalse();
        data.GetProperty("canResolveTicket").GetBoolean().Should().BeFalse();
        data.GetProperty("canFinishTicket").GetBoolean().Should().BeFalse();
        data.GetProperty("isSuperAdminTicket").GetBoolean().Should().BeFalse();
    }

    // ── /assignable ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assignable_ShouldReturnOnlyActiveResolversOfTheActiveTenant()
    {
        var tenant = await SeedTenantAsync();
        var resolver1 = await SeedResolverAsync(tenant, canResolve: true, canFinish: false, isSuperAdmin: false);
        var resolver2 = await SeedResolverAsync(tenant, canResolve: true, canFinish: true, isSuperAdmin: false);
        var nonResolver = await SeedResolverAsync(tenant, canResolve: false, canFinish: true, isSuperAdmin: true);
        var softDeleted = await SeedResolverAsync(tenant, canResolve: true, canFinish: true, isSuperAdmin: false, softDelete: true);

        // Member of a DIFFERENT company — must not appear.
        var otherTenant = await SeedTenantAsync(suffix: "X");
        var otherResolver = await SeedResolverAsync(otherTenant, canResolve: true, canFinish: false, isSuperAdmin: false);

        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.GetAsync("/api/v1/TicketUserCompanies/assignable");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var ids = Data(body).EnumerateArray()
            .Select(e => e.GetProperty("userId").GetGuid())
            .ToHashSet();

        ids.Should().BeEquivalentTo(new[] { resolver1.ResolverId, resolver2.ResolverId });
        ids.Should().NotContain(nonResolver.ResolverId);
        ids.Should().NotContain(softDeleted.ResolverId);
        ids.Should().NotContain(otherResolver.ResolverId);
    }

    [Fact]
    public async Task AssignableUser_ShouldBeAcceptedByCreateTicketAsInitialAssignee()
    {
        // SPEC 53 acceptance: anyone /assignable returns must not be rejected by
        // CreateTicket as INVALID_ASSIGNED_USER_NOT_RESOLVER.
        var tenant = await SeedTenantAsync();
        var resolver = await SeedResolverAsync(tenant, canResolve: true, canFinish: false, isSuperAdmin: false);

        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);
        var listResponse = await client.GetAsync("/api/v1/TicketUserCompanies/assignable");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
        var assignableUserId = Data(listBody).EnumerateArray()
            .Select(e => e.GetProperty("userId").GetGuid())
            .First();
        assignableUserId.Should().Be(resolver.ResolverId);

        // Now POST /api/v1/Tickets with that assignee — should succeed (no
        // INVALID_ASSIGNED_USER_NOT_RESOLVER).
        var createBody = new
        {
            name = "Ticket assigned via assignable list",
            description = "SPEC 53 acceptance",
            estimatedTime = 1m,
            consumedTime = 0m,
            ticketStatusId = tenant.InitialStatusId,
            ticketComplexityId = tenant.ComplexityId,
            timeUnitId = tenant.TimeUnitId,
            channelId = tenant.ChannelId,
            assignedToUserId = assignableUserId
        };
        var createResponse = await client.PostAsJsonAsync("/api/v1/Tickets", createBody);
        var raw = await createResponse.Content.ReadAsStringAsync();
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, raw);
        raw.Should().NotContain("INVALID_ASSIGNED_USER_NOT_RESOLVER");
        raw.Should().NotContain("INVALID_ASSIGNED_USER");
    }

    // ── MaxDayTicketInactivity ────────────────────────────────────────────────────────

    [Fact]
    public async Task TicketCompanyDefaults_ShouldExposeAndPersistMaxDayTicketInactivity()
    {
        var tenant = await SeedTenantAsync();
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var createResponse = await client.PostAsJsonAsync("/api/v1/TicketCompanyDefaults", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            ticketStatusDefaultId = tenant.InitialStatusId,
            maxDayTicketInactivity = 14
        });
        var createBody = await createResponse.Content.ReadAsStringAsync();
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created, createBody);
        Data(createBody).GetProperty("maxDayTicketInactivity").GetInt32().Should().Be(14);

        var listResponse = await client.GetAsync("/api/v1/TicketCompanyDefaults");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
        Data(listBody).EnumerateArray().Single()
            .GetProperty("maxDayTicketInactivity").GetInt32().Should().Be(14);

        var configId = Data(createBody).GetProperty("id").GetGuid();
        var updateResponse = await client.PutAsJsonAsync($"/api/v1/TicketCompanyDefaults/{configId}", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            ticketStatusDefaultId = tenant.InitialStatusId,
            maxDayTicketInactivity = (int?)null
        });
        var updateBody = await updateResponse.Content.ReadAsStringAsync();
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK, updateBody);
        Data(updateBody).GetProperty("maxDayTicketInactivity").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TicketCompanyDefaults_WhenMaxDayTicketInactivityIsNotPositive_ShouldReturn400(int value)
    {
        var tenant = await SeedTenantAsync();
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/TicketCompanyDefaults", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            maxDayTicketInactivity = value
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task TicketInactivity_StoredValue_ShouldBeReflectedInGetTickets()
    {
        // SPEC 53 acceptance: a ticket whose last activity is older than the
        // stored threshold appears with isInactive = true on GET /Tickets.
        var tenant = await SeedTenantAsync();
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var create = await client.PostAsJsonAsync("/api/v1/TicketCompanyDefaults", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            ticketStatusDefaultId = tenant.InitialStatusId,
            maxDayTicketInactivity = 7
        });
        create.EnsureSuccessStatusCode();
        var defaultId = Data(await create.Content.ReadAsStringAsync()).GetProperty("id").GetGuid();

        // Seed an old open ticket — same backdated-Created bypass as SPEC 52's tests.
        var ticketId = Guid.NewGuid();
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
            cmd.Parameters.AddWithValue("id", ticketId);
            cmd.Parameters.AddWithValue("company", tenant.CompanyId);
            cmd.Parameters.AddWithValue("code", $"S53-{ticketId:N}"[..14].ToUpperInvariant());
            cmd.Parameters.AddWithValue("name", "Spec53 old ticket");
            cmd.Parameters.AddWithValue("desc", "inactivity fixture");
            cmd.Parameters.AddWithValue("timeUnit", tenant.TimeUnitId);
            cmd.Parameters.AddWithValue("status", tenant.InProgressStatusId);
            cmd.Parameters.AddWithValue("complexity", tenant.ComplexityId);
            cmd.Parameters.AddWithValue("channel", tenant.ChannelId);
            cmd.Parameters.AddWithValue("user", tenant.OwnerUserId);
            cmd.Parameters.AddWithValue("created", DateTime.UtcNow.AddDays(-30));
            cmd.Parameters.AddWithValue("creator", Creator);
            await cmd.ExecuteNonQueryAsync();
        }

        var listResponse = await client.GetAsync("/api/v1/Tickets");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
        var item = Data(listBody).GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("id").GetGuid() == ticketId);
        item.GetProperty("isInactive").GetBoolean().Should().BeTrue();

        // Disable the threshold and re-check.
        var update = await client.PutAsJsonAsync($"/api/v1/TicketCompanyDefaults/{defaultId}", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            ticketStatusDefaultId = tenant.InitialStatusId,
            maxDayTicketInactivity = (int?)null
        });
        update.EnsureSuccessStatusCode();

        var listAfter = await client.GetAsync("/api/v1/Tickets");
        var itemAfter = Data(await listAfter.Content.ReadAsStringAsync())
            .GetProperty("items").EnumerateArray()
            .First(c => c.GetProperty("id").GetGuid() == ticketId);
        itemAfter.GetProperty("isInactive").GetBoolean().Should().BeFalse();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────

    private sealed record Tenant(
        Guid CompanyId,
        Guid OwnerUserId,
        Guid InitialStatusId,
        Guid InProgressStatusId,
        Guid FinalStatusId,
        Guid ComplexityId,
        Guid TimeUnitId,
        Guid ChannelId);

    private sealed record ResolverSeed(Guid ResolverId);

    private async Task<Tenant> SeedTenantAsync(string suffix = "")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var company = new Company
        {
            Name = $"JOIN-S53-{suffix}{Guid.NewGuid():N}"[..20],
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
            Code = 1,
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

        var ownerEmail = $"owner-{Guid.NewGuid():N}@t.local";
        var owner = new ApplicationUser
        {
            Email = ownerEmail,
            UserName = ownerEmail,
            FirstName = "Spec53",
            LastName = "Owner",
            EmailConfirmed = true,
            CreatedBy = Creator
        };
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        (await userManager.CreateAsync(owner, Password)).Succeeded.Should().BeTrue();
        await db.SaveChangesAsync();
        db.UserCompanies.Add(new UserCompany
        {
            UserId = owner.Id,
            CompanyId = company.Id,
            IsDefault = true,
            CreatedBy = Creator
        });
        await db.SaveChangesAsync();

        return new Tenant(company.Id, owner.Id, initial.Id, inProgress.Id, final.Id,
            complexity.Id, timeUnit.Id, channel.Id);
    }

    /// <summary>
    /// Registers a roster resolver (and a SuperAdmin owner) for the test
    /// tenant. The resolver user is returned so the test can log in as them
    /// (their JWT carries no role grants, so <c>DynamicAuthorizationFilter</c>
    /// would 403 everything — but <c>/me</c> only needs CanRead on
    /// <c>Tickets</c>, which the seeded roster does not provide, so we keep the
    /// caller tenant-bound without granting them roles).
    /// </summary>
    private async Task<ResolverSeed> SeedResolverAsync(
        Tenant tenant, bool canResolve, bool canFinish, bool isSuperAdmin, bool softDelete = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var resolverEmail = $"resolver-{Guid.NewGuid():N}@t.local";
        var resolver = new ApplicationUser
        {
            Email = resolverEmail,
            UserName = resolverEmail,
            FirstName = "Resolver",
            LastName = "User",
            EmailConfirmed = true,
            CreatedBy = Creator
        };
        (await userManager.CreateAsync(resolver, Password)).Succeeded.Should().BeTrue();
        await db.SaveChangesAsync();
        db.UserCompanies.Add(new UserCompany
        {
            UserId = resolver.Id,
            CompanyId = tenant.CompanyId,
            IsDefault = true,
            CreatedBy = Creator
        });
        var row = new TicketUserCompany
        {
            CompanyId = tenant.CompanyId,
            UserId = resolver.Id,
            CanResolveTicket = canResolve,
            CanFinishTicket = canFinish,
            IsSuperAdminTicket = isSuperAdmin,
            CreatedBy = Creator
        };
        db.TicketUserCompanies.Add(row);
        await db.SaveChangesAsync();
        if (softDelete)
        {
            row.MarkAsDeleted();
            await db.SaveChangesAsync();
        }

        return new ResolverSeed(resolver.Id);
    }

    private async Task<ApplicationUser> SeedUserAsync(Guid companyId, string label)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var email = $"{label}-{Guid.NewGuid():N}@t.local";
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            FirstName = label,
            LastName = "User",
            EmailConfirmed = true,
            CreatedBy = Creator
        };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await db.SaveChangesAsync();
        db.UserCompanies.Add(new UserCompany
        {
            UserId = user.Id,
            CompanyId = companyId,
            IsDefault = true,
            CreatedBy = Creator
        });
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Logs in as a fresh SuperAdmin bound to the test tenant.
    /// </summary>
    private async Task<HttpClient> CreateSuperAdminClientAsync(Guid companyId)
    {
        var email = $"admin-{Guid.NewGuid():N}@t.local";
        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec53",
            LastName = "Admin"
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
    /// Logs in as the supplied user (no role grants) bound to the tenant.
    /// Without <c>RoleSystemOption</c> entries on Tickets, the DynamicAuthorizationFilter
    /// would 403 — callers needing real CanRead on Tickets must use
    /// <see cref="CreateSuperAdminClientAsync"/> instead.
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(Guid companyId, ApplicationUser user)
    {
        using var unauthClient = _factory.CreateClient();
        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand
        {
            Email = user.Email!,
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
    /// Reads the caller user id from a logged-in client's JWT (the
    /// <c>http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier</c>
    /// claim — serialized by JWT as <c>sub</c>). Used to add a roster row for
    /// the SuperAdmin test caller so <c>/me</c> can resolve it.
    /// </summary>
    private static async Task<Guid> dbUserIdFromClientAsync(HttpClient client)
    {
        // Simplest path: ask the API for a self endpoint that returns the user
        // id. We don't have one, so decode the bearer token's 'sub' claim.
        var auth = client.DefaultRequestHeaders.Authorization!;
        var token = auth.Parameter!;
        var payloadPart = token.Split('.')[1];
        // Pad base64.
        var padded = payloadPart.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        using var doc = JsonDocument.Parse(bytes);
        var sub = doc.RootElement.GetProperty("sub").GetString();
        return Guid.Parse(sub!);
    }

    /// <summary>
    /// Adds an active <c>TicketUserCompany</c> row for <paramref name="userId"/>
    /// on <paramref name="companyId"/>. Used by <c>/me</c> tests to make the
    /// SuperAdmin test caller resolvable on the roster.
    /// </summary>
    private async Task AddRosterRowAsync(Guid companyId, Guid userId, bool canResolve, bool canFinish, bool isSuperAdmin)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TicketUserCompanies.Add(new TicketUserCompany
        {
            CompanyId = companyId,
            UserId = userId,
            CanResolveTicket = canResolve,
            CanFinishTicket = canFinish,
            IsSuperAdminTicket = isSuperAdmin,
            CreatedBy = Creator
        });
        await db.SaveChangesAsync();
    }

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}