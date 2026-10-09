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

namespace JOIN.IntegrationTests.Messaging;

/// <summary>
/// SPEC 42 — initial status of a new ticket through <c>POST /Tickets</c>: optional in the request,
/// falling back to <c>TicketCompanyDefaults.TicketStatusDefaultId</c>; a status sent by the client must
/// be active, not final and of the same company. Also covers the matching rule on
/// <c>POST /TicketCompanyDefaults</c> (<c>TICKET_STATUS_DEFAULT_INVALID</c>).
/// </summary>
public sealed class CreateTicketInitialStatusTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Creator = "spec42-tests";
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public CreateTicketInitialStatusTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WithoutStatus_ShouldUseTheConfiguredInitialStatus()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: true);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/Tickets", NewTicketBody(tenant, statusId: null));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        ReadData(body).GetProperty("ticketStatusId").GetGuid().Should().Be(tenant.InitialStatusId);
    }

    [Fact]
    public async Task Create_WithAnotherActiveNonFinalStatus_ShouldKeepTheRequestedStatus()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: true);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/Tickets", NewTicketBody(tenant, tenant.InProgressStatusId));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, body);
        ReadData(body).GetProperty("ticketStatusId").GetGuid().Should().Be(tenant.InProgressStatusId);
    }

    [Fact]
    public async Task Create_WithFinalStatus_ShouldReturnInvalidTicketStatus()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: true);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/Tickets", NewTicketBody(tenant, tenant.FinalStatusId));

        await ShouldFailAsync(response, "INVALID_TICKET_STATUS");
    }

    [Fact]
    public async Task Create_WithStatusOfAnotherCompany_ShouldReturnInvalidTicketStatus()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: true);
        var other = await SeedTenantAsync(configureDefaultStatus: true);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/Tickets", NewTicketBody(tenant, other.InitialStatusId));

        await ShouldFailAsync(response, "INVALID_TICKET_STATUS");
    }

    [Fact]
    public async Task Create_WithoutStatusAndWithoutConfiguredInitialStatus_ShouldReturnNotConfigured()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: false);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/Tickets", NewTicketBody(tenant, statusId: null));

        await ShouldFailAsync(response, "TICKET_DEFAULT_STATUS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task CreateDefaults_WithFinalStatus_ShouldReturnStatusDefaultInvalid()
    {
        var tenant = await SeedTenantAsync(configureDefaultStatus: false, seedDefaultsRow: false);
        using var client = await CreateSuperAdminClientAsync(tenant.CompanyId);

        var response = await client.PostAsJsonAsync("/api/v1/TicketCompanyDefaults", new
        {
            startCode = "TCK",
            codeSequenceLength = 6,
            usePersonalizedCode = false,
            ticketStatusDefaultId = tenant.FinalStatusId
        });

        await ShouldFailAsync(response, "TICKET_STATUS_DEFAULT_INVALID");
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private sealed record Tenant(
        Guid CompanyId,
        Guid InitialStatusId,
        Guid InProgressStatusId,
        Guid FinalStatusId,
        Guid ComplexityId,
        Guid TimeUnitId,
        Guid ChannelId);

    private static object NewTicketBody(Tenant tenant, Guid? statusId) => new
    {
        name = $"SPEC 42 {Guid.NewGuid():N}"[..20],
        description = "Initial status integration test",
        estimatedTime = 2m,
        consumedTime = 0m,
        ticketStatusId = statusId,
        ticketComplexityId = tenant.ComplexityId,
        timeUnitId = tenant.TimeUnitId,
        channelId = tenant.ChannelId
    };

    private static async Task ShouldFailAsync(HttpResponseMessage response, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("message").GetString().Should().Be(code);
    }

    private static JsonElement ReadData(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private async Task<Tenant> SeedTenantAsync(bool configureDefaultStatus, bool seedDefaultsRow = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var company = new Company
        {
            Name = $"JOIN-S42-{Guid.NewGuid():N}"[..20],
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
        var timeUnit = new TimeUnit { CompanyId = company.Id, Name = $"U{Guid.NewGuid():N}"[..20], Code = 1, CreatedBy = Creator };
        var complexity = new TicketComplexity
        {
            CompanyId = company.Id,
            Name = $"X{Guid.NewGuid():N}"[..20],
            Code = Random.Shared.Next(1000, 999_999),
            ResolutionTimeUnits = 8,
            TimeUnitId = timeUnit.Id,
            CreatedBy = Creator
        };
        var channel = new CommunicationChannel { Name = $"CH{Guid.NewGuid():N}"[..20], IsActive = true, CreatedBy = Creator };
        db.AddRange(initial, inProgress, final, timeUnit, complexity, channel);

        if (seedDefaultsRow)
        {
            db.Add(new TicketCompanyDefault
            {
                CompanyId = company.Id,
                StartCode = "TCK",
                CodeSequenceLength = 6,
                TicketStatusDefaultId = configureDefaultStatus ? initial.Id : null,
                CreatedBy = Creator
            });
        }

        await db.SaveChangesAsync();
        return new Tenant(company.Id, initial.Id, inProgress.Id, final.Id, complexity.Id, timeUnit.Id, channel.Id);
    }

    /// <summary>
    /// Registers a SuperAdmin member of <paramref name="companyId"/> and returns a client logged in as it,
    /// so the request reaches the handlers without per-resource grants.
    /// </summary>
    private async Task<HttpClient> CreateSuperAdminClientAsync(Guid companyId)
    {
        var email = $"u{Guid.NewGuid():N}@t.local";

        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec42",
            LastName = "SuperAdmin",
        })).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();

            db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
            (await userManager.AddToRoleAsync(user, "SuperAdmin")).Succeeded.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var login = await unauthClient.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        payload!.Data!.Token.Should().NotBeNullOrWhiteSpace();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Data.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return client;
    }
}
