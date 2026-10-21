// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.IntegrationTests.Customers;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Catalog;

/// <summary>
/// SPEC 70 — product images and documents against PostgreSQL 17 (acceptance criteria): the 5-image cap, one primary
/// enforced by the filtered unique index, signature checks, company formats and sizes, <c>CanDownload</c>, tenant
/// isolation, inactive products, the seeded limits, the separate <c>ProductSettings</c> resource, the cascade of a
/// product delete/restore, <c>PrimaryImageId</c> in the lists and lookups, and the <c>EXPLAIN</c> of the new SQL.
/// </summary>
public sealed class Spec70ProductImagesAndDocumentsPostgreSqlTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Images_FirstIsPrimary_SixthIsRejected_AndListsCarryThePrimaryImage()
    {
        var (companyId, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);

        var first = await CommercialTestHelpers.DataAsync(await UploadImageAsync(admin, productId, "a.png", Png));
        first.GetProperty("isPrimary").GetBoolean().Should().BeTrue();
        first.GetProperty("width").GetInt32().Should().Be(3);
        for (var i = 1; i < 5; i++)
        {
            (await UploadImageAsync(admin, productId, $"img{i}.png", Png)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var sixth = await UploadImageAsync(admin, productId, "f.png", Png);
        sixth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("PRODUCT_IMAGE_LIMIT");

        var primaryId = first.GetProperty("id").GetGuid();
        var list = await CommercialTestHelpers.DataAsync(await admin.GetAsync("/api/v1/Products"));
        var row = Items(list).Single(p => p.GetProperty("id").GetGuid() == productId);
        row.GetProperty("primaryImageId").GetGuid().Should().Be(primaryId);
        row.GetProperty("imageCount").GetInt32().Should().Be(5);
        (await CommercialTestHelpers.DataAsync(await admin.GetAsync($"/api/v1/Products/{productId}"))).GetProperty("imageCount").GetInt32().Should().Be(5);

        var lookup = await CommercialTestHelpers.DataAsync(await admin.GetAsync("/api/v1/Leads/products-lookup"));
        lookup.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == productId).GetProperty("primaryImageId").GetGuid().Should().Be(primaryId);

        foreach (var url in new[] { $"/api/v1/Products/{productId}/images/{primaryId}/content", $"/api/v1/Leads/products-lookup/{productId}/images/{primaryId}/content" })
        {
            var content = await admin.GetAsync(url);
            content.StatusCode.Should().Be(HttpStatusCode.OK, url);
            content.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            content.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
            content.Headers.CacheControl!.Private.Should().BeTrue();
            content.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
            (await content.Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        }

        _ = companyId;
    }

    [Fact]
    public async Task Primary_IsUniqueInTheDatabase_AndDeletingItPromotesTheNextBySortOrder()
    {
        var (_, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await CommercialTestHelpers.DataAsync(await UploadImageAsync(admin, productId, $"i{i}.png", Png))).GetProperty("id").GetGuid());
        }

        (await admin.PutAsync($"/api/v1/Products/{productId}/images/{ids[1]}/primary", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PrimaryIdsAsync(productId)).Should().Equal(ids[1]);

        (await admin.PutAsJsonAsync($"/api/v1/Products/{productId}/images/order", new { imageIds = new[] { ids[1], ids[2], ids[0] } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/Products/{productId}/images/order", new { imageIds = new[] { ids[1] } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await admin.DeleteAsync($"/api/v1/Products/{productId}/images/{ids[1]}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PrimaryIdsAsync(productId)).Should().Equal(ids[2]);

        // SuperAdmin restores the old primary: it does not steal the flag back.
        (await admin.PostAsync($"/api/v1/Products/{productId}/images/{ids[1]}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PrimaryIdsAsync(productId)).Should().Equal(ids[2]);
    }

    [Fact]
    public async Task Images_SignatureAndCompanyFormatsAreEnforced()
    {
        var (_, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);

        await ExpectErrorAsync(UploadImageAsync(admin, productId, "photo.jpg", Pdf), HttpStatusCode.BadRequest, "PRODUCT_IMAGE_FORMAT_NOT_ALLOWED");
        await ExpectErrorAsync(UploadImageAsync(admin, productId, "photo.jpg", Png), HttpStatusCode.BadRequest, "PRODUCT_IMAGE_FORMAT_NOT_ALLOWED");

        await PutSettingsAsync(admin, allowedImageFormats: 1);
        await ExpectErrorAsync(UploadImageAsync(admin, productId, "photo.webp", Webp), HttpStatusCode.BadRequest, "PRODUCT_IMAGE_FORMAT_NOT_ALLOWED");
        (await UploadImageAsync(admin, productId, "photo.jpg", Jpeg)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Documents_LimitsDownloadPermissionAndTenantIsolation()
    {
        var (companyId, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);
        var typeId = await DocumentTypeIdAsync(admin, "TECH_SHEET");

        await ExpectErrorAsync(UploadDocumentAsync(admin, productId, typeId, "setup.exe"), HttpStatusCode.BadRequest, "PRODUCT_DOCUMENT_FILE_TYPE_NOT_ALLOWED");
        var document = await CommercialTestHelpers.DataAsync(await UploadDocumentAsync(admin, productId, typeId, "ficha.pdf"));
        document.GetProperty("productDocumentTypeName").GetString().Should().Be("Ficha técnica");
        var documentId = document.GetProperty("id").GetGuid();

        await PutSettingsAsync(admin, maxDocumentSizeBytes: 2);
        await ExpectErrorAsync(UploadDocumentAsync(admin, productId, typeId, "otra.pdf"), HttpStatusCode.BadRequest, "PRODUCT_DOCUMENT_TOO_LARGE");

        var download = await admin.GetAsync($"/api/v1/Products/{productId}/documents/{documentId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);

        // Products read without CanDownload → 403.
        var (reader, _) = await CommercialTestHelpers.AddUserAsync(factory, companyId, "Supervisor");
        await CommercialTestHelpers.GrantAsync(factory, companyId, "Supervisor", "Products", create: false, update: false, delete: false);
        (await reader.GetAsync($"/api/v1/Products/{productId}/documents")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/v1/Products/{productId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Another company's document → 404.
        var (_, other, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        (await other.GetAsync($"/api/v1/Products/{productId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/Products/{productId}/images")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InactiveProduct_RejectsUploads_ButKeepsShowingAndDownloading()
    {
        var (_, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);
        var imageId = (await CommercialTestHelpers.DataAsync(await UploadImageAsync(admin, productId, "a.png", Png))).GetProperty("id").GetGuid();
        var typeId = await DocumentTypeIdAsync(admin, "TECH_SHEET");
        var documentId = (await CommercialTestHelpers.DataAsync(await UploadDocumentAsync(admin, productId, typeId, "f.pdf"))).GetProperty("id").GetGuid();
        await ExecuteAsync("UPDATE catalog.products SET isactive = FALSE WHERE id = {0}", productId);

        await ExpectErrorAsync(UploadImageAsync(admin, productId, "b.png", Png), HttpStatusCode.Conflict, "PRODUCT_NOT_ACTIVE");
        await ExpectErrorAsync(UploadDocumentAsync(admin, productId, typeId, "g.pdf"), HttpStatusCode.Conflict, "PRODUCT_NOT_ACTIVE");
        (await admin.GetAsync($"/api/v1/Products/{productId}/images")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/v1/Products/{productId}/images/{imageId}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/v1/Products/{productId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Settings_NewCompanyIsSeeded_AndWithoutTheRowNothingUploads()
    {
        var (companyId, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);

        var settings = await CommercialTestHelpers.DataAsync(await admin.GetAsync("/api/v1/ProductAttachmentSettings"));
        settings.GetProperty("maxImagesPerProduct").GetInt32().Should().Be(5);
        settings.GetProperty("maxDocumentsPerProduct").GetInt32().Should().Be(20);
        settings.GetProperty("allowedImageFormatNames").EnumerateArray().Select(e => e.GetString()).Should().BeEquivalentTo(["Jpeg", "Png", "Webp"]);
        var codes = (await CommercialTestHelpers.DataAsync(await admin.GetAsync("/api/v1/ProductDocumentTypes"))).EnumerateArray()
            .Select(e => e.GetProperty("code").GetString()).ToList();
        codes.Should().BeEquivalentTo(["TECH_SHEET", "PROPOSAL", "MANUAL", "OTHER"]);
        (await admin.PostAsJsonAsync("/api/v1/ProductAttachmentSettings", SettingsBody())).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var productId = await CreateProductAsync(admin);
        await ExecuteAsync("UPDATE catalog.productattachmentsettings SET gcrecord = 20260101 WHERE companyid = {0}", companyId);
        await ExpectErrorAsync(UploadImageAsync(admin, productId, "a.png", Png), HttpStatusCode.BadRequest, "PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED");
    }

    [Fact]
    public async Task ProductSettings_IsASeparateResource()
    {
        var (companyId, _, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var (editor, _) = await CommercialTestHelpers.AddUserAsync(factory, companyId, "Supervisor");
        await CommercialTestHelpers.GrantAsync(factory, companyId, "Supervisor", "Products");

        (await editor.GetAsync("/api/v1/Products")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await editor.PutAsJsonAsync("/api/v1/ProductAttachmentSettings", SettingsBody())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PostAsJsonAsync("/api/v1/ProductDocumentTypes", new { code = "X", name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteAndRestoreProduct_CascadesToImagesAndDocuments_KeepingTheFiles()
    {
        var (_, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);
        var imageId = (await CommercialTestHelpers.DataAsync(await UploadImageAsync(admin, productId, "a.png", Png))).GetProperty("id").GetGuid();
        var typeId = await DocumentTypeIdAsync(admin, "TECH_SHEET");
        var documentId = (await CommercialTestHelpers.DataAsync(await UploadDocumentAsync(admin, productId, typeId, "f.pdf"))).GetProperty("id").GetGuid();

        (await admin.DeleteAsync($"/api/v1/Products/{productId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GcRecordAsync("productimages", imageId)).Should().NotBe(0);
        (await GcRecordAsync("productdocuments", documentId)).Should().NotBe(0);

        (await admin.PostAsync($"/api/v1/Products/{productId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GcRecordAsync("productimages", imageId)).Should().Be(0);
        (await GcRecordAsync("productdocuments", documentId)).Should().Be(0);
        (await (await admin.GetAsync($"/api/v1/Products/{productId}/images/{imageId}/content")).Content.ReadAsByteArrayAsync()).Should().Equal(Png);
        (await (await admin.GetAsync($"/api/v1/Products/{productId}/documents/{documentId}/download")).Content.ReadAsByteArrayAsync()).Should().Equal(Pdf);
    }

    /// <summary>Step 6 — <c>EXPLAIN</c> of the new SQL: no sequential scan on the image and document tables.</summary>
    [Fact]
    public async Task Explain_NewQueriesUseTheIndexes()
    {
        var (companyId, admin, _) = await CommercialTestHelpers.CreateCompanyWithUserAsync(factory);
        var productId = await CreateProductAsync(admin);
        string[] queries =
        [
            "SELECT i.Id FROM catalog.productimages i LEFT JOIN security.users u ON u.Id = i.UploadedByUserId WHERE i.ProductId = @p AND i.CompanyId = @c AND i.GcRecord = 0 ORDER BY i.SortOrder",
            "SELECT i.Path FROM catalog.productimages i WHERE i.Id = @p AND i.ProductId = @p AND i.CompanyId = @c AND i.GcRecord = 0",
            "SELECT d.Id FROM catalog.productdocuments d LEFT JOIN catalog.productdocumenttypes t ON t.Id = d.ProductDocumentTypeId WHERE d.ProductId = @p AND d.CompanyId = @c AND d.GcRecord = 0 ORDER BY d.Created DESC",
            "SELECT d.Path FROM catalog.productdocuments d WHERE d.Id = @p AND d.ProductId = @p AND d.CompanyId = @c AND d.GcRecord = 0",
            "SELECT p.Id, img.Id, CAST((SELECT COUNT(*) FROM catalog.productimages ic WHERE ic.CompanyId = p.CompanyId AND ic.ProductId = p.Id AND ic.GcRecord = 0) AS int) FROM catalog.products p LEFT JOIN catalog.productimages img ON img.ProductId = p.Id AND img.IsPrimary = TRUE AND img.GcRecord = 0 WHERE p.CompanyId = @c AND p.GcRecord = 0"
        ];

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using (var off = connection.CreateCommand())
        {
            // Tiny tables: force the planner to show whether an index is usable at all.
            off.CommandText = "SET enable_seqscan = off";
            await off.ExecuteNonQueryAsync();
        }

        foreach (var sql in queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "EXPLAIN " + sql.Replace("@p", $"'{productId}'").Replace("@c", $"'{companyId}'");
            var plan = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }

            var text = string.Join(Environment.NewLine, plan);
            text.Should().NotContain("Seq Scan on productimages", sql);
            text.Should().NotContain("Seq Scan on productdocuments", sql);
        }
    }

    // ---- helpers ----

    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x02, 0x08, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xD9];

    private static readonly byte[] Webp = [0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x58];

    private static readonly byte[] Pdf = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A];

    private static object SettingsBody(int allowedImageFormats = 7, long maxDocumentSizeBytes = 10L * 1024 * 1024) => new
    {
        allowedDocumentTypes = 1 | 2 | 4 | 16, // Pdf | Word | Excel | Image
        maxDocumentSizeBytes,
        maxDocumentsPerProduct = 20,
        allowedImageFormats,
        maxImageSizeBytes = 5L * 1024 * 1024,
        maxImagesPerProduct = 5
    };

    private static async Task PutSettingsAsync(HttpClient client, int allowedImageFormats = 7, long maxDocumentSizeBytes = 10L * 1024 * 1024) =>
        await CommercialTestHelpers.DataAsync(await client.PutAsJsonAsync("/api/v1/ProductAttachmentSettings", SettingsBody(allowedImageFormats, maxDocumentSizeBytes)));

    private static async Task<Guid> CreateProductAsync(HttpClient client) =>
        (await CommercialTestHelpers.DataAsync(await client.PostAsJsonAsync("/api/v1/Products",
            new { code = $"SKU-{Guid.NewGuid():N}"[..16], name = "Producto", kind = 1, referencePrice = 1m })))
        .GetProperty("id").GetGuid();

    private static async Task<Guid> DocumentTypeIdAsync(HttpClient client, string code) =>
        (await CommercialTestHelpers.DataAsync(await client.GetAsync("/api/v1/ProductDocumentTypes"))).EnumerateArray()
        .Single(e => e.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> UploadImageAsync(HttpClient client, Guid productId, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        return client.PostAsync($"/api/v1/Products/{productId}/images", form);
    }

    private static Task<HttpResponseMessage> UploadDocumentAsync(HttpClient client, Guid productId, Guid typeId, string fileName)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(typeId.ToString()), "productDocumentTypeId");
        form.Add(new StringContent("Ficha del producto"), "displayName");
        return client.PostAsync($"/api/v1/Products/{productId}/documents", form);
    }

    private static async Task ExpectErrorAsync(Task<HttpResponseMessage> call, HttpStatusCode status, string code)
    {
        var response = await call;
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        body.Should().Contain(code);
    }

    private static IEnumerable<JsonElement> Items(JsonElement data) =>
        data.ValueKind == JsonValueKind.Array ? data.EnumerateArray() : data.GetProperty("items").EnumerateArray();

    private async Task<List<Guid>> PrimaryIdsAsync(Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ProductImages.IgnoreQueryFilters().Where(i => i.ProductId == productId && i.GcRecord == 0 && i.IsPrimary).Select(i => i.Id).ToListAsync();
    }

    private async Task<int> GcRecordAsync(string table, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return table == "productimages"
            ? await db.ProductImages.IgnoreQueryFilters().Where(i => i.Id == id).Select(i => i.GcRecord).SingleAsync()
            : await db.ProductDocuments.IgnoreQueryFilters().Where(d => d.Id == id).Select(d => d.GcRecord).SingleAsync();
    }

    private async Task ExecuteAsync(string sql, Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql, id);
    }
}
