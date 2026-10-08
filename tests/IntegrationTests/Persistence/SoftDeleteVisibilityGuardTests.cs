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

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 41 — visibility guard over every listing endpoint of the implemented stages (Etapa 1: the
/// 19 catalogs). For each endpoint it seeds an active and a deleted row in company A and an active row
/// in company B (global catalogs: an active and a deleted row), then checks the visibility invariants:
/// <list type="bullet">
/// <item>Manager / SuperAdminCompany of A with <c>includeDeleted=true</c> → only the active row of A
/// (the flag is ignored and nothing leaks from B). SuperAdmin-only endpoints answer 403 instead.</item>
/// <item>SuperAdmin (active company A) with <c>includeDeleted=true</c> → active and deleted rows of A, not B.</item>
/// <item>SuperAdmin with <c>includeDeleted=true&amp;companyId=B</c> → the row of B.</item>
/// </list>
/// It also covers <c>POST /{id}/restore</c>: Manager → 403; SuperAdmin → 200 and the row is back in the
/// normal listing. A new Dapper listing that forgets its tenant predicate fails here (the gap SPEC 38 left).
/// The <c>system-wide</c> endpoints are excluded on purpose (SPEC 41, Etapa 1 decision 2026-10-07).
/// </summary>
public sealed class SoftDeleteVisibilityGuardTests : IClassFixture<CustomWebApplicationFactory>
{
    private const int DeletedStamp = 20260101;
    private const string Password = "Integration!Pass123";

    private readonly CustomWebApplicationFactory _factory;

    public SoftDeleteVisibilityGuardTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<string> CaseNames => new(Cases.Keys);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Listing_ForNonSuperAdminRoles_ShouldIgnoreIncludeDeletedAndNeverLeakOtherCompany(string caseName)
    {
        var testCase = Cases[caseName];
        var (companyA, companyB) = await SeedCompaniesAsync();
        var rows = await SeedRowsAsync(testCase, companyA, companyB);

        foreach (var role in new[] { "Manager", "SuperAdminCompany" })
        {
            using var client = await CreateClientAsync(companyA, role);

            var response = await GetListAsync(client, testCase, rows.Tag, includeDeleted: true, companyId: companyB);

            if (testCase.SuperAdminOnly)
            {
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{caseName} is restricted to SuperAdmin ({role})");
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} as {role}: {await response.Content.ReadAsStringAsync()}");
            var ids = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: companyB);
            ids.Should().BeEquivalentTo(new[] { rows.ActiveA }, $"{caseName} as {role} must only see the active row of its own company");
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Listing_ForSuperAdmin_ShouldIncludeDeletedOnlyWhenRequestedAndStayInResolvedCompany(string caseName)
    {
        var testCase = Cases[caseName];
        var (companyA, companyB) = await SeedCompaniesAsync();
        var rows = await SeedRowsAsync(testCase, companyA, companyB);
        using var client = await CreateClientAsync(companyA, "SuperAdmin");

        var byDefault = await ListIdsAsync(client, testCase, rows, includeDeleted: false, companyId: null);
        byDefault.Should().BeEquivalentTo(new[] { rows.ActiveA }, $"{caseName}: deleted rows are opt-in even for SuperAdmin");

        var withDeleted = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: null);
        withDeleted.Should().BeEquivalentTo(new[] { rows.ActiveA, rows.DeletedA }, $"{caseName}: SuperAdmin sees active and deleted rows of the resolved company");

        if (testCase.TenantScoped)
        {
            var otherCompany = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: companyB);
            otherCompany.Should().BeEquivalentTo(new[] { rows.ActiveB!.Value }, $"{caseName}: SuperAdmin can request another company explicitly");
        }
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Restore_AsManagerIsForbidden_AsSuperAdminBringsTheRowBack(string caseName)
    {
        var testCase = Cases[caseName];
        var (companyA, companyB) = await SeedCompaniesAsync();
        var rows = await SeedRowsAsync(testCase, companyA, companyB);

        using (var manager = await CreateClientAsync(companyA, "Manager"))
        {
            var forbidden = await manager.PostAsync(testCase.RestoreUrl(rows.DeletedA), content: null);
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{caseName}: only SuperAdmin can restore");
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        var restored = await superAdmin.PostAsync(testCase.RestoreUrl(rows.DeletedA), content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName}: {await restored.Content.ReadAsStringAsync()}");

        var listed = await ListIdsAsync(superAdmin, testCase, rows, includeDeleted: false, companyId: null);
        listed.Should().BeEquivalentTo(new[] { rows.ActiveA, rows.DeletedA }, $"{caseName}: the restored row is back in the normal listing");

        var again = await superAdmin.PostAsync(testCase.RestoreUrl(rows.DeletedA), content: null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{caseName}: restoring an active row answers NOT_DELETED");
    }

    [Fact]
    public async Task Restore_WhenAnActiveDuplicateExists_ShouldAnswerConflictNeverServerError()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        Gender deleted;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var tag = $"v{Guid.NewGuid():N}"[..12];
            deleted = Gender.Create(companyA, "DUP", tag);
            deleted.GcRecord = DeletedStamp;
            deleted.CreatedBy = Creator;
            var active = Gender.Create(companyA, "DUP", $"{tag}x");
            active.CreatedBy = Creator;
            db.AddRange(deleted, active);
            await db.SaveChangesAsync();
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        var response = await superAdmin.PostAsync($"/api/v1/Genders/{deleted.Id}/restore", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("ACTIVE_DUPLICATE_EXISTS");
    }

    // ── catalog of listing endpoints ─────────────────────────────────────────────────────────────

    private sealed record SeededRows(string Tag, Guid ActiveA, Guid DeletedA, Guid? ActiveB);

    /// <summary>
    /// One listing endpoint. <paramref name="Seed"/> inserts the rows through EF (tenant rows for A and B,
    /// or global rows) and returns their ids; the tag is a unique name fragment used as the list filter.
    /// <paramref name="CompanyByHeader"/>: the explicit company travels in X-Company-Id (Area, Project).
    /// </summary>
    private sealed record VisibilityCase(
        string Resource,
        string Route,
        string FilterParameter,
        bool TenantScoped,
        Func<ApplicationDbContext, string, Guid, Guid, Task<SeededRows>> Seed,
        bool SuperAdminOnly = false,
        bool CompanyByHeader = false)
    {
        public string ListUrl(string tag, bool includeDeleted, Guid? companyId, int page = 1)
        {
            var url = $"/api/v1/{Route}?pageNumber={page}&pageSize=100&{FilterParameter}={tag}";
            if (includeDeleted)
            {
                url += "&includeDeleted=true";
            }

            if (companyId is { } explicitCompany)
            {
                url += $"&companyId={explicitCompany}";
            }

            return url;
        }

        public string RestoreUrl(Guid id) => $"/api/v1/{Route}/{id}/restore";
    }

    private static readonly IReadOnlyDictionary<string, VisibilityCase> Cases = new Dictionary<string, VisibilityCase>
    {
        ["Gender"] = new("Genders", "Genders", "name", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => Gender.Create(company, name[^6..], name), a, b)),
        ["Industry"] = new("Industries", "Industries", "name", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => Industry.Create(company, name[^6..], name, null), a, b)),
        ["TaxRegime"] = new("TaxRegimes", "TaxRegimes", "name", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => TaxRegime.Create(company, name[^6..], name), a, b)),
        ["IncomeRange"] = new("IncomeRanges", "IncomeRanges", "displayName", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => IncomeRange.Create(company, name, 0m, 1000m, "USD", Random.Shared.Next(1, 1_000_000)), a, b)),
        ["Area"] = new("Areas", "Area", "name", true, async (db, tag, a, b) =>
        {
            var status = await SeedEntityStatusAsync(db);
            return await SeedTenantAsync(db, tag, (company, name) => new Area { CompanyId = company, Name = name, EntityStatusId = status }, a, b);
        }, CompanyByHeader: true),
        ["Project"] = new("Projects", "Projects", "name", true, async (db, tag, a, b) =>
        {
            var status = await SeedEntityStatusAsync(db);
            return await SeedTenantAsync(db, tag, (company, name) => new Project { CompanyId = company, Name = name, EntityStatusId = status }, a, b);
        }, CompanyByHeader: true),
        ["TicketStatus"] = new("TicketStatuses", "TicketStatuses", "name", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => new TicketStatus { CompanyId = company, Name = name, Code = Random.Shared.Next(1000, 9999) }, a, b)),
        ["TicketComplexity"] = new("TicketComplexities", "TicketComplexities", "name", true, async (db, tag, a, b) =>
        {
            var units = new Dictionary<Guid, Guid>();
            foreach (var company in new[] { a, b })
            {
                var unit = new TimeUnit { CompanyId = company, Name = $"U{tag}", Code = Random.Shared.Next(1000, 9999), CreatedBy = Creator };
                db.Add(unit);
                units[company] = unit.Id;
            }

            await db.SaveChangesAsync();
            return await SeedTenantAsync(db, tag, (company, name) => new TicketComplexity
            {
                CompanyId = company,
                Name = name,
                Code = Random.Shared.Next(1000, 9999),
                TimeUnitId = units[company]
            }, a, b);
        }),
        ["TimeUnit"] = new("TimeUnits", "TimeUnits", "name", true, (db, tag, a, b) => SeedTenantAsync(db, tag,
            (company, name) => new TimeUnit { CompanyId = company, Name = name, Code = Random.Shared.Next(1000, 9999) }, a, b)),
        ["Region"] = new("Regions", "Regions", "name", true, async (db, tag, a, b) =>
        {
            var country = await SeedCountryAsync(db);
            return await SeedTenantAsync(db, tag, (company, name) => new Region { CompanyId = company, CountryId = country, Name = name }, a, b);
        }),
        ["Country"] = new("Countries", "Country", "searchTerm", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new Country { Name = name, IsoCode = $"V{Guid.NewGuid():N}"[..10] })),
        ["Province"] = new("Provinces", "Provinces", "name", false, async (db, tag, _, _) =>
        {
            var country = await SeedCountryAsync(db);
            return await SeedGlobalAsync(db, tag, name => new Province { Name = name, Code = $"P{Guid.NewGuid():N}"[..12], CountryId = country });
        }),
        ["Municipality"] = new("Municipalities", "Municipalities", "name", false, async (db, tag, _, _) =>
        {
            var country = await SeedCountryAsync(db);
            var province = new Province { Name = $"P{tag}", Code = $"P{Guid.NewGuid():N}"[..12], CountryId = country, CreatedBy = Creator };
            db.Add(province);
            await db.SaveChangesAsync();
            return await SeedGlobalAsync(db, tag, name => new Municipality { Name = name, ProvinceId = province.Id });
        }),
        ["StreetType"] = new("StreetTypes", "StreetTypes", "searchTerm", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new StreetType { Name = name, Abbreviation = $"S{Guid.NewGuid():N}"[..10] })),
        ["CommunicationChannel"] = new("CommunicationChannels", "CommunicationChannels", "searchTerm", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new CommunicationChannel { Name = name, IsActive = true })),
        ["IdentificationType"] = new("IdentificationTypes", "IdentificationTypes", "name", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new IdentificationType { Name = name })),
        ["EntityStatus"] = new("EntityStatuses", "EntityStatuses", "entityStatuses", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new EntityStatus { Name = name, Code = Random.Shared.Next(100_000, 999_999) })),
        ["SystemModule"] = new("SystemModules", "SystemModules", "name", false, (db, tag, _, _) => SeedGlobalAsync(db, tag,
            name => new SystemModule { Name = name }), SuperAdminOnly: true),
        ["SystemOption"] = new("SystemOptions", "SystemOptions", "name", false, async (db, tag, _, _) =>
        {
            var module = new SystemModule { Name = $"M{tag}", CreatedBy = Creator };
            db.Add(module);
            await db.SaveChangesAsync();
            return await SeedGlobalAsync(db, tag, name => new SystemOption { ModuleId = module.Id, Name = name, Route = $"/{name}" });
        }, SuperAdminOnly: true),
    };

    private const string Creator = nameof(SoftDeleteVisibilityGuardTests);

    private static async Task<SeededRows> SeedTenantAsync<T>(ApplicationDbContext db, string tag, Func<Guid, string, T> create, Guid companyA, Guid companyB)
        where T : BaseTenantEntity
    {
        // Distinct natural keys (tag + suffix) so restoring the deleted row never meets an active
        // duplicate; that conflict has its own test. The list filter (the tag) matches all three.
        var activeA = create(companyA, $"{tag}a");
        var deletedA = create(companyA, $"{tag}d");
        deletedA.GcRecord = DeletedStamp;
        var activeB = create(companyB, $"{tag}b");
        foreach (var row in new[] { activeA, deletedA, activeB })
        {
            row.CreatedBy = Creator;
            db.Add(row);
        }

        await db.SaveChangesAsync();
        return new SeededRows(tag, activeA.Id, deletedA.Id, activeB.Id);
    }

    private static async Task<SeededRows> SeedGlobalAsync<T>(ApplicationDbContext db, string tag, Func<string, T> create)
        where T : BaseAuditableEntity
    {
        // Two different names sharing the tag: global unique indexes (Name, IsoCode...) are not
        // filtered by GcRecord, so the deleted row cannot reuse the active row's key.
        var active = create($"{tag}a");
        var deleted = create($"{tag}d");
        deleted.GcRecord = DeletedStamp;
        foreach (var row in new[] { active, deleted })
        {
            row.CreatedBy = Creator;
            db.Add(row);
        }

        await db.SaveChangesAsync();
        return new SeededRows(tag, active.Id, deleted.Id, null);
    }

    private static async Task<Guid> SeedEntityStatusAsync(ApplicationDbContext db)
    {
        var status = new EntityStatus { Name = $"ES{Guid.NewGuid():N}"[..20], Code = Random.Shared.Next(100_000, 999_999), CreatedBy = Creator };
        db.Add(status);
        await db.SaveChangesAsync();
        return status.Id;
    }

    private static async Task<Guid> SeedCountryAsync(ApplicationDbContext db)
    {
        var country = new Country { Name = $"C{Guid.NewGuid():N}"[..20], IsoCode = $"C{Guid.NewGuid():N}"[..10], CreatedBy = Creator };
        db.Add(country);
        await db.SaveChangesAsync();
        return country.Id;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<SeededRows> SeedRowsAsync(VisibilityCase testCase, Guid companyA, Guid companyB)
    {
        // The tag is the list filter: unique per test so seeded data (and other tests) never match it.
        var tag = $"v{Guid.NewGuid():N}"[..12];
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await testCase.Seed(db, tag, companyA, companyB);
    }

    private async Task<(Guid CompanyA, Guid CompanyB)> SeedCompaniesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var companies = new[] { "A", "B" }.Select(letter => new Company
        {
            Name = $"JOIN-V{letter}-{Guid.NewGuid():N}"[..20],
            TaxId = $"RUC{letter}{Guid.NewGuid():N}"[..16],
            IsActive = true,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = Creator
        }).ToArray();
        db.Companies.AddRange(companies);
        await db.SaveChangesAsync();
        return (companies[0].Id, companies[1].Id);
    }

    /// <summary>
    /// Registers a user that belongs to <paramref name="companyId"/> with the Identity role
    /// <paramref name="roleName"/> and logs in. Non-SuperAdmin roles also get a <see cref="UserRoleCompany"/>
    /// in the company and read/create grants on every resource of <see cref="Cases"/>, so the
    /// DynamicAuthorizationFilter lets the request reach the handler (where the visibility rules apply).
    /// </summary>
    private async Task<HttpClient> CreateClientAsync(Guid companyId, string roleName)
    {
        var email = $"u{Guid.NewGuid():N}@t.local";

        using var unauthClient = _factory.CreateClient();
        (await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Visibility",
            LastName = roleName,
        })).EnsureSuccessStatusCode();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();

            db.UserCompanies.Add(new UserCompany { UserId = user!.Id, CompanyId = companyId, IsDefault = true, CreatedBy = Creator });
            (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

            if (roleName != "SuperAdmin")
            {
                var role = await db.Set<ApplicationRole>().SingleAsync(r => r.Name == roleName);
                db.Set<UserRoleCompany>().Add(new UserRoleCompany { UserId = user.Id, RoleId = role.Id, CompanyId = companyId, CreatedBy = Creator });

                var module = new SystemModule { Name = $"VM{Guid.NewGuid():N}"[..20], CreatedBy = Creator };
                db.Add(module);
                foreach (var resource in Cases.Values.Select(c => c.Resource).Distinct())
                {
                    var option = new SystemOption
                    {
                        ModuleId = module.Id,
                        Name = resource,
                        Route = $"/visibility/{resource}/{Guid.NewGuid():N}",
                        ControllerName = resource,
                        CreatedBy = Creator
                    };
                    db.Add(option);
                    db.Add(new RoleSystemOption
                    {
                        RoleId = role.Id,
                        SystemOptionId = option.Id,
                        CompanyId = companyId,
                        CanRead = true,
                        CanCreate = true,
                        CreatedBy = Creator
                    });
                }
            }

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

    /// <summary>
    /// Sends the listing request. The explicit company goes in <c>?companyId=</c>, except for
    /// <see cref="VisibilityCase.CompanyByHeader"/> endpoints (Area, Project), where it replaces the
    /// X-Company-Id header — the vector a non-SuperAdmin used to read another company before SPEC 41.
    /// </summary>
    private static async Task<HttpResponseMessage> GetListAsync(
        HttpClient client, VisibilityCase testCase, string tag, bool includeDeleted, Guid? companyId, int page = 1)
    {
        if (!testCase.CompanyByHeader)
        {
            return await client.GetAsync(testCase.ListUrl(tag, includeDeleted, companyId, page));
        }


        using var request = new HttpRequestMessage(HttpMethod.Get, testCase.ListUrl(tag, includeDeleted, companyId: null, page));
        if (companyId is { } explicitCompany)
        {
            var original = client.DefaultRequestHeaders.GetValues("X-Company-Id").Single();
            client.DefaultRequestHeaders.Remove("X-Company-Id");
            client.DefaultRequestHeaders.Add("X-Company-Id", explicitCompany.ToString());
            try
            {
                return await client.SendAsync(request);
            }
            finally
            {
                client.DefaultRequestHeaders.Remove("X-Company-Id");
                client.DefaultRequestHeaders.Add("X-Company-Id", original);
            }
        }

        return await client.SendAsync(request);
    }

    /// <summary>
    /// Reads every page of the listing and returns the ids of the seeded rows it contains. Endpoints
    /// without a name filter (SystemOptions) spread the rows over several pages; rows that are not
    /// seeded by the test are ignored, while a leaked row of company B (seeded) is still caught.
    /// </summary>
    private static async Task<IReadOnlyList<Guid>> ListIdsAsync(
        HttpClient client, VisibilityCase testCase, SeededRows rows, bool includeDeleted, Guid? companyId)
    {
        var ids = new List<Guid>();
        for (var page = 1; ; page++)
        {
            using var response = await GetListAsync(client, testCase, rows.Tag, includeDeleted, companyId, page);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.GetProperty("data");
            ids.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            if (page >= data.GetProperty("totalPages").GetInt32())
            {
                break;
            }
        }

        return ids.Where(id => id == rows.ActiveA || id == rows.DeletedA || id == rows.ActiveB).ToList();
    }
}
