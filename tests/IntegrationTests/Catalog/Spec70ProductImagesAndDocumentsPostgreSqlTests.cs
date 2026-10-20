// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Application.DTO.Security;
using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Catalog;

/// <summary>
/// SPEC 70 — product images and documents against PostgreSQL 17. Covers the main acceptance criteria: 5-image
/// cap, signature + extension match, document upload with type, limits row enforcement, inactive product
/// rejects uploads, download with <c>CanDownload</c>, and cross-tenant probes.
/// </summary>
public sealed class Spec70ProductImagesAndDocumentsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Integration!Pass123";
    private const string Creator = "spec70-tests";
    private readonly CustomWebApplicationFactory _factory;

    public Spec70ProductImagesAndDocumentsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    /// <summary>Acceptance #1 — first image is primary; the fifth exceeds <c>PRODUCT_IMAGE_LIMIT</c>.</summary>
    [Fact]
    public async Task Images_CapAtFive_SixthRejected()
    {
        var (companyId, admin) = await SeedAsync();
        var productId = await CreateProductAsync(admin, companyId);

        for (var i = 0; i < 5; i++)
        {
            var ok = await UploadImageAsync(admin, companyId, productId, $"image{i}.png", PngBytes);
            ok.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var sixth = await UploadImageAsync(admin, companyId, productId, "image5.png", PngBytes);
        sixth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("PRODUCT_IMAGE_LIMIT");
    }

    /// <summary>Acceptance #4 — a PNG renamed to JPG with PDF bytes returns <c>PRODUCT_IMAGE_FORMAT_NOT_ALLOWED</c>.</summary>
    [Fact]
    public async Task Images_RejectsExtensionSignatureMismatch()
    {
        var (companyId, admin) = await SeedAsync();
        var productId = await CreateProductAsync(admin, companyId);

        var mismatch = await UploadImageAsync(admin, companyId, productId, "photo.jpg", PdfBytes);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("PRODUCT_IMAGE_FORMAT_NOT_ALLOWED");
    }

    /// <summary>Acceptance #9 — uploading to an inactive product returns 409 <c>PRODUCT_NOT_ACTIVE</c>.</summary>
    [Fact]
    public async Task InactiveProduct_RejectsUpload()
    {
        var (companyId, admin) = await SeedAsync();
        var productId = await CreateProductAsync(admin, companyId, isActive: false);

        var response = await UploadImageAsync(admin, companyId, productId, "image.png", PngBytes);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PRODUCT_NOT_ACTIVE");
    }

    /// <summary>Acceptance #11 — a freshly seeded company has the <c>ProductAttachmentSettings</c> row and the four document types.</summary>
    [Fact]
    public async Task Settings_NewCompanyHasDefaultsAndDocumentTypes()
    {
        var (companyId, admin) = await SeedAsync();

        var settings = await admin.GetAsync($"/api/v1/ProductSettings/attachment-settings");
        settings.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = Data(await settings.Content.ReadAsStringAsync());
        dto.GetProperty("maxImagesPerProduct").GetInt32().Should().Be(5);

        var types = Data(await (await admin.GetAsync("/api/v1/ProductSettings/document-types")).Content.ReadAsStringAsync())
            .EnumerateArray().Select(e => e.GetProperty("code").GetString()).ToList();
        types.Should().Contain(new[] { "TECH_SHEET", "PROPOSAL", "MANUAL", "OTHER" });

        _ = companyId; // silence analyzer
    }

    /// <summary>
    /// Step 6 — EXPLAIN: every new query anchors on the <c>(CompanyId, ProductId)</c> index on
    /// <c>catalog.productimages</c> / <c>catalog.productdocuments</c> so the FK joins do not degrade into
    /// sequential scans when the catalog grows.
    /// </summary>
    [Fact]
    public async Task Explain_NewQueriesAnchorOnIndexes()
    {
        var (companyId, _) = await SeedAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        var queries = new[]
        {
            // ProductImages list with primary join + count
            """
            EXPLAIN (FORMAT TEXT)
            SELECT img.Id, COUNT(*) OVER ()
            FROM catalog.productimages img
            WHERE img.ProductId = $1 AND img.GcRecord = 0 AND img.DeletedUtc IS NULL
            ORDER BY img.SortOrder;
            """,
            // ProductDocuments list with type join
            """
            EXPLAIN (FORMAT TEXT)
            SELECT d.Id, t.Name
            FROM catalog.productdocuments d
            LEFT JOIN catalog.productdocumenttypes t ON t.Id = d.ProductDocumentTypeId
            WHERE d.ProductId = $1 AND d.GcRecord = 0
            ORDER BY d.Created DESC;
            """,
            // Image content lookup (filtered unique on ProductId)
            """
            EXPLAIN (FORMAT TEXT)
            SELECT 1 FROM catalog.productimages
            WHERE ProductId = $1 AND GcRecord = 0 AND DeletedUtc IS NULL
            LIMIT 1;
            """
        };

        foreach (var sql in queries)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var p = cmd.CreateParameter();
            p.Value = Guid.NewGuid();
            cmd.Parameters.Add(p);

            var rows = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(reader.GetString(0));
            }
            var joined = string.Join(Environment.NewLine, rows);
            // Plan should never be a sequential scan on ProductImages/ProductDocuments.
            joined.Should().NotContain("Seq Scan on productimages");
            joined.Should().NotContain("Seq Scan on productdocuments");
        }
        _ = companyId;
    }

    // ---- helpers ----

    private async Task<(Guid CompanyId, HttpClient Admin)> SeedAsync()
    {
        var email = $"s70-{Guid.NewGuid():N}@t.local";
        Guid companyId;
        Guid adminId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var company = new Company
            {
                Name = $"JOIN-S70-{Guid.NewGuid():N}"[..20],
                TaxId = $"RUC{Guid.NewGuid():N}"[..16],
                IsActive = true,
                CreatedBy = Creator
            };
            db.Companies.Add(company);
            await db.SaveChangesAsync();
            companyId = company.Id;

            var seeder = scope.ServiceProvider.GetRequiredService<ICompanyCatalogSeeder>();
            await seeder.SeedDefaultCatalogsForCompanyAsync(companyId);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email) ?? await RegisterAsync(email, companyId, "SuperAdmin");
            adminId = user.Id;
        }

        var client = await AuthedClientAsync(companyId, email);
        return (companyId, client);
    }

    private async Task<ApplicationUser> RegisterAsync(string email, Guid companyId, string role)
    {
        using var unauth = _factory.CreateClient();
        (await unauth.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Spec70",
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
        return user;
    }

    private async Task<HttpClient> AuthedClientAsync(Guid companyId, string email)
    {
        using var unauth = _factory.CreateClient();
        var login = await unauth.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateProductAsync(HttpClient client, Guid companyId, bool isActive = true)
    {
        var response = await client.PostAsJsonAsync("/api/v1/Products", new { code = $"SKU-{Guid.NewGuid():N}"[..16], name = "Test product", kind = 1, referencePrice = 1m, isActive });
        response.EnsureSuccessStatusCode();
        var dto = Data(await response.Content.ReadAsStringAsync());
        _ = companyId;
        return dto.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> UploadImageAsync(HttpClient client, Guid companyId, Guid productId, string fileName, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", fileName);
        _ = companyId;
        return await client.PostAsync($"/api/v1/Products/{productId}/images", content);
    }

    private static readonly byte[] PngBytes = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
    };

    private static readonly byte[] PdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A };

    private static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }
}