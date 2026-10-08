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
using JOIN.Domain.Enums;
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

            if (testCase.SuperAdminOnly || (testCase.ManagerForbidden && role == "Manager"))
            {
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{caseName} is restricted by role ({role})");
                continue;
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, $"{caseName} as {role}: {await response.Content.ReadAsStringAsync()}");
            var ids = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: companyB);
            ids.Should().BeEquivalentTo(new[] { rows.ActiveA }, $"{caseName} as {role} must only see the active row of its own company");

            if (rows.TagB is not null)
            {
                var otherPerson = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: companyB, useCompanyBKey: true);
                otherPerson.Should().BeEmpty($"{caseName} as {role} must not read the children of another company's person");
            }
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

        if (testCase.TenantScoped && testCase.CompanyOverride)
        {
            var otherCompany = await ListIdsAsync(client, testCase, rows, includeDeleted: true, companyId: companyB, useCompanyBKey: true);
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

    [Fact]
    public async Task DeletePerson_ShouldCascadeToItsAddress_AndRestorePersonShouldBringItBack()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var (personId, addressId) = await SeedPersonWithAddressAsync(companyA);
        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");

        var deleted = await superAdmin.DeleteAsync($"/api/v1/Persons/{personId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());

        var (personStamp, addressStamp) = await ReadStampsAsync(personId, addressId);
        personStamp.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        addressStamp.Should().Be(personStamp, "SPEC 41 (2026-10-08): composition children share the cascade stamp");

        var restored = await superAdmin.PostAsync($"/api/v1/Persons/{personId}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());

        (await ReadStampsAsync(personId, addressId)).Should().Be((BaseAuditableEntity.ActiveGcRecord, BaseAuditableEntity.ActiveGcRecord),
            "restoring the person brings back the children of its cascade");
    }

    [Fact]
    public async Task DeletePerson_WithAnActiveCustomer_ShouldAnswerConflictAndDeleteNothing()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var (personId, addressId) = await SeedPersonWithAddressAsync(companyA);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"c{Guid.NewGuid():N}"[..12], Email = $"c{Guid.NewGuid():N}"[..12] + "@t.local" };
            user.NormalizedUserName = user.UserName!.ToUpperInvariant();
            user.NormalizedEmail = user.Email!.ToUpperInvariant();
            user.SecurityStamp = Guid.NewGuid().ToString();
            db.Add(user);
            var customer = Customer.Create(companyA, personId, user.Id, $"C{Guid.NewGuid():N}"[..10], PersonLifecycleStage.Lead);
            customer.CreatedBy = Creator;
            db.Add(customer);
            await db.SaveChangesAsync();
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        var response = await superAdmin.DeleteAsync($"/api/v1/Persons/{personId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PERSON_IN_USE").And.Contain("Active customers: 1");
        (await ReadStampsAsync(personId, addressId)).Should().Be((BaseAuditableEntity.ActiveGcRecord, BaseAuditableEntity.ActiveGcRecord),
            "a blocking reference stops the whole cascade");
    }

    private async Task<(Guid PersonId, Guid AddressId)> SeedPersonWithAddressAsync(Guid companyId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var identificationType = await SeedIdentificationTypeAsync(db);
        var personId = await SeedPersonAsync(db, companyId, identificationType);
        var catalogs = await SeedAddressCatalogsAsync(db);
        var address = new PersonAddress
        {
            CompanyId = companyId,
            PersonId = personId,
            AddressLine1 = "Main street",
            ZipCode = "11001",
            StreetTypeId = catalogs.StreetType,
            CountryId = catalogs.Country,
            ProvinceId = catalogs.Province,
            MunicipalityId = catalogs.Municipality,
            CreatedBy = Creator
        };
        db.Add(address);
        await db.SaveChangesAsync();
        return (personId, address.Id);
    }

    private async Task<(int Person, int Address)> ReadStampsAsync(Guid personId, Guid addressId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var person = await db.Persons.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == personId);
        var address = await db.Set<PersonAddress>().IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == addressId);
        return (person.GcRecord, address.GcRecord);
    }

    // ── Etapa 3: empresas, módulos de empresa, membresías y borrado de roles/empresas ──

    [Fact]
    public async Task Companies_DeletedCompanyIsVisibleOnlyToSuperAdmin_AndCanBeRestored()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var tag = $"v{Guid.NewGuid():N}"[..12];
        Guid deletedId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deleted = new Company { Name = $"{tag}d", TaxId = $"T{Guid.NewGuid():N}"[..16], IsActive = true, GcRecord = DeletedStamp, CreatedBy = Creator };
            db.Add(deleted);
            await db.SaveChangesAsync();
            deletedId = deleted.Id;
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        (await PagedIdsAsync(superAdmin, $"/api/v1/Companies?pageNumber=1&pageSize=100&searchTerm={tag}")).Should().NotContain(deletedId);
        (await PagedIdsAsync(superAdmin, $"/api/v1/Companies?pageNumber=1&pageSize=100&searchTerm={tag}&includeDeleted=true")).Should().Contain(deletedId);

        // GET /Companies is SuperAdmin/SuperAdminCompany only; a company admin sees its own company, never deleted ones.
        using (var companyAdmin = await CreateClientAsync(companyA, "SuperAdminCompany"))
        {
            (await PagedIdsAsync(companyAdmin, $"/api/v1/Companies?pageNumber=1&pageSize=100&searchTerm={tag}&includeDeleted=true")).Should().NotContain(deletedId);
        }

        var restored = await superAdmin.PostAsync($"/api/v1/Companies/{deletedId}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await PagedIdsAsync(superAdmin, $"/api/v1/Companies?pageNumber=1&pageSize=100&searchTerm={tag}")).Should().Contain(deletedId);
    }

    [Fact]
    public async Task DeleteCompany_WithActiveData_ShouldAnswerConflict_WhileAnEmptyCompanyIsDeleted()
    {
        var (companyA, companyB) = await SeedCompaniesAsync();
        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");

        // companyA holds the SuperAdmin's own membership (UserCompany) -> active data.
        var inUse = await superAdmin.DeleteAsync($"/api/v1/Companies/{companyA}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inUse.Content.ReadAsStringAsync()).Should().Contain("COMPANY_IN_USE").And.Contain("Active UserCompany: 1");

        var empty = await superAdmin.DeleteAsync($"/api/v1/Companies/{companyB}");
        empty.StatusCode.Should().Be(HttpStatusCode.OK, await empty.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CompanyModules_DeletedAssignmentIsVisibleToSuperAdmin_AndCanBeRestored()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        Guid assignmentId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var module = new SystemModule { Name = $"VM{Guid.NewGuid():N}"[..20], CreatedBy = Creator };
            db.Add(module);
            var assignment = new CompanyModule { CompanyId = companyA, ModuleId = module.Id, GcRecord = DeletedStamp, CreatedBy = Creator };
            db.Add(assignment);
            await db.SaveChangesAsync();
            assignmentId = assignment.Id;
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        var listUrl = $"/api/v1/CompanyModules/by-admin?companyId={companyA}&pageNumber=1&pageSize=100";
        (await PagedIdsAsync(superAdmin, listUrl)).Should().NotContain(assignmentId);
        (await PagedIdsAsync(superAdmin, listUrl + "&includeDeleted=true")).Should().Contain(assignmentId);

        var restored = await superAdmin.PostAsync($"/api/v1/CompanyModules/{assignmentId}/restore?companyId={companyA}", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await PagedIdsAsync(superAdmin, listUrl)).Should().Contain(assignmentId);
    }

    [Fact]
    public async Task UserCompanies_RemovedMembershipIsVisibleToSuperAdmin_AndRestoreBringsItsRolesBack()
    {
        var (companyA, companyB) = await SeedCompaniesAsync();
        Guid userId;
        Guid assignmentId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"m{Guid.NewGuid():N}"[..12], Email = $"m{Guid.NewGuid():N}"[..12] + "@t.local" };
            user.NormalizedUserName = user.UserName!.ToUpperInvariant();
            user.NormalizedEmail = user.Email!.ToUpperInvariant();
            user.SecurityStamp = Guid.NewGuid().ToString();
            var role = NewRole($"v{Guid.NewGuid():N}"[..12]);
            db.AddRange(user, role);
            db.Add(new UserCompany { UserId = user.Id, CompanyId = companyA, IsDefault = true, CreatedBy = Creator });
            // Removed membership in B together with its role assignment (same RemoveUserCompany stamp).
            db.Add(new UserCompany { UserId = user.Id, CompanyId = companyB, GcRecord = DeletedStamp, CreatedBy = Creator });
            var assignment = new UserRoleCompany { UserId = user.Id, CompanyId = companyB, RoleId = role.Id, GcRecord = DeletedStamp, CreatedBy = Creator };
            db.Add(assignment);
            await db.SaveChangesAsync();
            userId = user.Id;
            assignmentId = assignment.Id;
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        (await MembershipCompaniesAsync(superAdmin, userId, includeDeleted: false)).Should().BeEquivalentTo(new[] { companyA });
        (await MembershipCompaniesAsync(superAdmin, userId, includeDeleted: true)).Should().BeEquivalentTo(new[] { companyA, companyB });

        using (var manager = await CreateClientAsync(companyA, "Manager"))
        {
            // GET /Users/{id}/companies is SuperAdmin-only (pre-existing); a Manager cannot read memberships at all.
            var forbidden = await manager.GetAsync($"/api/v1/Users/{userId}/companies?includeDeleted=true");
            forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var restored = await superAdmin.PostAsync($"/api/v1/Users/{userId}/companies/{companyB}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await MembershipCompaniesAsync(superAdmin, userId, includeDeleted: false)).Should().BeEquivalentTo(new[] { companyA, companyB });

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await verifyDb.Set<UserRoleCompany>().IgnoreQueryFilters().AsNoTracking().SingleAsync(urc => urc.Id == assignmentId))
            .GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord, "the role removed with the membership comes back with it");
    }

    [Fact]
    public async Task DeleteRole_ShouldCascadeToPermissionsAndLinks_AndRestoreRoleBringsThemBack()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var (roleId, permissionId, linkId) = await SeedRoleWithChildrenAsync(companyA);

        using (var companyAdmin = await CreateClientAsync(companyA, "SuperAdminCompany"))
        {
            var deleted = await companyAdmin.DeleteAsync($"/api/v1/Roles/{roleId}");
            deleted.StatusCode.Should().Be(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        }

        var stamps = await ReadRoleStampsAsync(roleId, permissionId, linkId);
        stamps.Role.Should().BeGreaterThan(0);
        stamps.Permission.Should().Be(stamps.Role, "permissions are composition of the role");
        stamps.Link.Should().Be(stamps.Role, "company links are composition of the role");

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        var restored = await superAdmin.PostAsync($"/api/v1/Roles/{roleId}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await ReadRoleStampsAsync(roleId, permissionId, linkId)).Should().Be((0, 0, 0));
    }

    [Fact]
    public async Task DeleteRole_WithUsersInAnotherCompany_ShouldAnswerConflict()
    {
        var (companyA, companyB) = await SeedCompaniesAsync();
        var (roleId, _, _) = await SeedRoleWithChildrenAsync(companyA);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync();
            db.Add(new UserRoleCompany { UserId = user.Id, CompanyId = companyB, RoleId = roleId, CreatedBy = Creator });
            await db.SaveChangesAsync();
        }

        using var companyAdmin = await CreateClientAsync(companyA, "SuperAdminCompany");
        var response = await companyAdmin.DeleteAsync($"/api/v1/Roles/{roleId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "SPEC 41: users in ANY company block the delete");
        (await response.Content.ReadAsStringAsync()).Should().Contain("ROLE_HAS_USERS");
    }

    private async Task<(Guid RoleId, Guid PermissionId, Guid LinkId)> SeedRoleWithChildrenAsync(Guid companyId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = NewRole($"v{Guid.NewGuid():N}"[..12]);
        var module = new SystemModule { Name = $"VM{Guid.NewGuid():N}"[..20], CreatedBy = Creator };
        db.AddRange(role, module);
        var option = new SystemOption { ModuleId = module.Id, Name = "Opt", Route = $"/v/{Guid.NewGuid():N}", CreatedBy = Creator };
        db.Add(option);
        var permission = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option.Id, CompanyId = companyId, CanRead = true, CreatedBy = Creator };
        var link = new RoleCompany { RoleId = role.Id, CompanyId = companyId, CreatedBy = Creator };
        db.AddRange(permission, link);
        await db.SaveChangesAsync();
        return (role.Id, permission.Id, link.Id);
    }

    private async Task<(int Role, int Permission, int Link)> ReadRoleStampsAsync(Guid roleId, Guid permissionId, Guid linkId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var role = await db.Set<ApplicationRole>().IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == roleId);
        var permission = await db.Set<RoleSystemOption>().IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == permissionId);
        var link = await db.Set<RoleCompany>().IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == linkId);
        return (role.GcRecord, permission.GcRecord, link.GcRecord);
    }

    private static async Task<IReadOnlyList<Guid>> PagedIdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task<IReadOnlyList<Guid>> MembershipCompaniesAsync(HttpClient client, Guid userId, bool includeDeleted)
    {
        var response = await client.GetAsync($"/api/v1/Users/{userId}/companies" + (includeDeleted ? "?includeDeleted=true" : string.Empty));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("companyId").GetGuid())
            .ToList();
    }

    // ── catalog of listing endpoints ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeded ids. <c>Tag</c> is the list key: a name fragment for filtered listings, or the person id of
    /// company A for per-person child listings (<c>TagB</c> is then the person of company B).
    /// </summary>
    private sealed record SeededRows(string Tag, Guid ActiveA, Guid DeletedA, Guid? ActiveB, string? TagB = null);

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
        bool CompanyByHeader = false,
        string? RestoreRoute = null,
        bool CompanyOverride = true,
        bool ManagerForbidden = false)
    {
        /// <summary>
        /// Builds the listing URL. A route with a <c>{key}</c> placeholder (per-person child listings) takes
        /// the key in the path and no name filter.
        /// </summary>
        public string ListUrl(string key, bool includeDeleted, Guid? companyId, int page = 1)
        {
            var url = Route.Contains("{key}")
                ? $"/api/v1/{Route.Replace("{key}", key)}?pageNumber={page}"
                : string.IsNullOrEmpty(FilterParameter)
                    ? $"/api/v1/{Route}?pageNumber={page}&pageSize=100"
                    : $"/api/v1/{Route}?pageNumber={page}&pageSize=100&{FilterParameter}={key}";
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

        public string RestoreUrl(Guid id) => $"/api/v1/{RestoreRoute ?? Route}/{id}/restore";
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
        // ── Etapa 3: seguridad y empresas ──
        ["Role"] = new("Roles", "Roles/detailed", "name", false, async (db, tag, _, _) =>
        {
            var active = NewRole($"{tag}a");
            var deleted = NewRole($"{tag}d");
            deleted.GcRecord = DeletedStamp;
            db.AddRange(active, deleted);
            await db.SaveChangesAsync();
            return new SeededRows(tag, active.Id, deleted.Id, null);
        }, RestoreRoute: "Roles", ManagerForbidden: true),
        ["RoleCompany"] = new("RoleCompanies", "RoleCompanies", string.Empty, true, async (db, tag, a, b) =>
        {
            var roleA = NewRole($"{tag}r1");
            var roleB = NewRole($"{tag}r2");
            db.AddRange(roleA, roleB);
            // Different roles so restoring the deleted link never meets an active (RoleId, CompanyId) duplicate.
            var activeA = new RoleCompany { RoleId = roleA.Id, CompanyId = a, CreatedBy = Creator };
            var deletedA = new RoleCompany { RoleId = roleB.Id, CompanyId = a, GcRecord = DeletedStamp, CreatedBy = Creator };
            var activeB = new RoleCompany { RoleId = roleA.Id, CompanyId = b, CreatedBy = Creator };
            db.AddRange(activeA, deletedA, activeB);
            await db.SaveChangesAsync();
            return new SeededRows(tag, activeA.Id, deletedA.Id, activeB.Id);
        }, ManagerForbidden: true),
        ["RoleSystemOption"] = new("RoleSystemOption", "RoleSystemOptions", string.Empty, true, async (db, tag, a, b) =>
        {
            var role = NewRole($"{tag}r");
            var module = new SystemModule { Name = $"M{tag}", CreatedBy = Creator };
            db.AddRange(role, module);
            var option1 = new SystemOption { ModuleId = module.Id, Name = $"{tag}1", Route = $"/{tag}/1", CreatedBy = Creator };
            var option2 = new SystemOption { ModuleId = module.Id, Name = $"{tag}2", Route = $"/{tag}/2", CreatedBy = Creator };
            db.AddRange(option1, option2);
            var activeA = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option1.Id, CompanyId = a, CanRead = true, CreatedBy = Creator };
            var deletedA = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option2.Id, CompanyId = a, CanRead = true, GcRecord = DeletedStamp, CreatedBy = Creator };
            var activeB = new RoleSystemOption { RoleId = role.Id, SystemOptionId = option1.Id, CompanyId = b, CanRead = true, CreatedBy = Creator };
            db.AddRange(activeA, deletedA, activeB);
            await db.SaveChangesAsync();
            return new SeededRows(tag, activeA.Id, deletedA.Id, activeB.Id);
        }, CompanyOverride: false),
        // ── Etapa 2: personas ──
        ["Person"] = new("Persons", "Persons", "firstName", true, async (db, tag, a, b) =>
        {
            var identificationType = await SeedIdentificationTypeAsync(db);
            return await SeedTenantAsync(db, tag, (company, name) => new Person
            {
                CompanyId = company,
                FirstName = name,
                LastName = "Doe",
                IdentificationTypeId = identificationType,
                IdentificationNumber = name
            }, a, b);
        }),
        ["Customer"] = new("Customers", "Customers", "customerCode", true, async (db, tag, a, b) =>
        {
            var identificationType = await SeedIdentificationTypeAsync(db);
            async Task<Customer> NewCustomerAsync(Guid company, string code, int stamp)
            {
                var person = await SeedPersonAsync(db, company, identificationType);
                var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"c{Guid.NewGuid():N}"[..12], Email = $"c{Guid.NewGuid():N}"[..12] + "@t.local" };
                user.NormalizedUserName = user.UserName!.ToUpperInvariant();
                user.NormalizedEmail = user.Email!.ToUpperInvariant();
                user.SecurityStamp = Guid.NewGuid().ToString();
                db.Add(user);
                var customer = Customer.Create(company, person, user.Id, code, PersonLifecycleStage.Lead);
                customer.GcRecord = stamp;
                customer.CreatedBy = Creator;
                db.Add(customer);
                return customer;
            }

            // CustomerCode allows 10 characters: a 9-char tag + the a/d/b suffix.
            var code = tag[..9];
            var activeA = await NewCustomerAsync(a, $"{code}a", 0);
            var deletedA = await NewCustomerAsync(a, $"{code}d", DeletedStamp);
            var activeB = await NewCustomerAsync(b, $"{code}b", 0);
            await db.SaveChangesAsync();
            return new SeededRows(code, activeA.Id, deletedA.Id, activeB.Id);
        }),
        ["PersonAddress"] = PersonChildCase("PersonAddress", async (db, company, person) =>
        {
            var catalogs = await SeedAddressCatalogsAsync(db);
            return company => new PersonAddress
            {
                CompanyId = company,
                PersonId = person[company],
                AddressLine1 = "Main street",
                ZipCode = "11001",
                StreetTypeId = catalogs.StreetType,
                CountryId = catalogs.Country,
                ProvinceId = catalogs.Province,
                MunicipalityId = catalogs.Municipality
            };
        }),
        ["PersonContact"] = PersonChildCase("PersonContact", (_, _, person) => Task.FromResult<Func<Guid, BaseTenantEntity>>(
            company => PersonContact.Create(company, person[company], ContactType.PrimaryEmail, $"{Guid.NewGuid():N}"[..10] + "@t.local"))),
        ["PersonEmployment"] = PersonChildCase("PersonEmployment", (_, _, person) => Task.FromResult<Func<Guid, BaseTenantEntity>>(
            company => PersonEmployment.Create(company, person[company], "JOIN", "Engineer", new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)))),
        ["PersonBusinessProfile"] = PersonChildCase("PersonBusinessProfile", async (db, companies, person) =>
        {
            var catalogs = new Dictionary<Guid, (Guid Industry, Guid TaxRegime)>();
            foreach (var company in companies)
            {
                var industry = Industry.Create(company, $"I{Guid.NewGuid():N}"[..8], $"I{Guid.NewGuid():N}"[..12], null);
                var taxRegime = TaxRegime.Create(company, $"T{Guid.NewGuid():N}"[..8], $"T{Guid.NewGuid():N}"[..12]);
                industry.CreatedBy = Creator;
                taxRegime.CreatedBy = Creator;
                db.AddRange(industry, taxRegime);
                catalogs[company] = (industry.Id, taxRegime.Id);
            }

            await db.SaveChangesAsync();
            return company => PersonBusinessProfile.Create(company, person[company], catalogs[company].Industry, catalogs[company].TaxRegime);
        }),
        ["PersonFinancialProfile"] = PersonChildCase("PersonFinancialProfile", async (db, companies, person) =>
        {
            var ranges = new Dictionary<Guid, Guid>();
            foreach (var company in companies)
            {
                var range = IncomeRange.Create(company, $"R{Guid.NewGuid():N}"[..12], 0m, 1000m, "USD", Random.Shared.Next(1, 1_000_000));
                range.CreatedBy = Creator;
                db.Add(range);
                ranges[company] = range.Id;
            }

            await db.SaveChangesAsync();
            return company => PersonFinancialProfile.Create(company, person[company], ranges[company], "Salary", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }),
    };

    private const string Creator = nameof(SoftDeleteVisibilityGuardTests);

    /// <summary>
    /// A per-person child listing (<c>GET /{route}/person/{personId}</c>): one active person per company;
    /// the children are an active and a deleted row of the person of A and an active row of the person of B.
    /// <paramref name="childFactory"/> receives the companies (A, B) and the person of each company.
    /// </summary>
    private static VisibilityCase PersonChildCase(
        string route,
        Func<ApplicationDbContext, Guid[], IReadOnlyDictionary<Guid, Guid>, Task<Func<Guid, BaseTenantEntity>>> childFactory)
        => new("Persons", route + "/person/{key}", string.Empty, true, async (db, _, a, b) =>
        {
            var identificationType = await SeedIdentificationTypeAsync(db);
            var person = new Dictionary<Guid, Guid>
            {
                [a] = await SeedPersonAsync(db, a, identificationType),
                [b] = await SeedPersonAsync(db, b, identificationType)
            };
            await db.SaveChangesAsync();

            var create = await childFactory(db, new[] { a, b }, person);
            var activeA = create(a);
            var deletedA = create(a);
            deletedA.GcRecord = DeletedStamp;
            var activeB = create(b);
            foreach (var row in new[] { activeA, deletedA, activeB })
            {
                row.CreatedBy = Creator;
                db.Add(row);
            }

            await db.SaveChangesAsync();
            return new SeededRows(person[a].ToString(), activeA.Id, deletedA.Id, activeB.Id, person[b].ToString());
        }, RestoreRoute: route);

    private static ApplicationRole NewRole(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
        CreatedBy = Creator
    };

    private static async Task<Guid> SeedIdentificationTypeAsync(ApplicationDbContext db)
    {
        var identificationType = new IdentificationType { Name = $"IT{Guid.NewGuid():N}"[..20], CreatedBy = Creator };
        db.Add(identificationType);
        await db.SaveChangesAsync();
        return identificationType.Id;
    }

    private static Task<Guid> SeedPersonAsync(ApplicationDbContext db, Guid company, Guid identificationType)
    {
        var person = new Person
        {
            CompanyId = company,
            FirstName = "Jane",
            LastName = "Doe",
            IdentificationTypeId = identificationType,
            IdentificationNumber = $"P{Guid.NewGuid():N}"[..15],
            CreatedBy = Creator
        };
        db.Add(person);
        return Task.FromResult(person.Id);
    }

    private static async Task<(Guid StreetType, Guid Country, Guid Province, Guid Municipality)> SeedAddressCatalogsAsync(ApplicationDbContext db)
    {
        var streetType = new StreetType { Name = $"S{Guid.NewGuid():N}"[..20], Abbreviation = $"S{Guid.NewGuid():N}"[..10], CreatedBy = Creator };
        var country = new Country { Name = $"C{Guid.NewGuid():N}"[..20], IsoCode = $"C{Guid.NewGuid():N}"[..10], CreatedBy = Creator };
        db.AddRange(streetType, country);
        var province = new Province { Name = $"P{Guid.NewGuid():N}"[..20], Code = $"P{Guid.NewGuid():N}"[..12], CountryId = country.Id, CreatedBy = Creator };
        db.Add(province);
        var municipality = new Municipality { Name = $"M{Guid.NewGuid():N}"[..20], ProvinceId = province.Id, CreatedBy = Creator };
        db.Add(municipality);
        await db.SaveChangesAsync();
        return (streetType.Id, country.Id, province.Id, municipality.Id);
    }

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
                foreach (var resource in Cases.Values.Select(c => c.Resource).Concat(new[] { "Companies", "CompanyModules", "Users" }).Distinct())
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
                        CanUpdate = true,
                        CanDelete = true,
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
        HttpClient client, VisibilityCase testCase, string key, bool includeDeleted, Guid? companyId, int page = 1)
    {
        if (!testCase.CompanyByHeader)
        {
            return await client.GetAsync(testCase.ListUrl(key, includeDeleted, companyId, page));
        }


        using var request = new HttpRequestMessage(HttpMethod.Get, testCase.ListUrl(key, includeDeleted, companyId: null, page));
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
        HttpClient client, VisibilityCase testCase, SeededRows rows, bool includeDeleted, Guid? companyId, bool useCompanyBKey = false)
    {
        var key = useCompanyBKey && rows.TagB is not null ? rows.TagB : rows.Tag;
        var ids = new List<Guid>();
        for (var page = 1; ; page++)
        {
            using var response = await GetListAsync(client, testCase, key, includeDeleted, companyId, page);
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, body);

            using var document = JsonDocument.Parse(body);
            var data = document.RootElement.GetProperty("data");
            if (data.ValueKind == JsonValueKind.Array)
            {
                // Per-person child listings return a plain list (not paged).
                ids.AddRange(data.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
                break;
            }

            ids.AddRange(data.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            if (page >= data.GetProperty("totalPages").GetInt32())
            {
                break;
            }
        }

        return ids.Where(id => id == rows.ActiveA || id == rows.DeletedA || id == rows.ActiveB).ToList();
    }
}
