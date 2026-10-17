// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static JOIN.IntegrationTests.Customers.CommercialTestHelpers;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// SPEC 62 — lead documents against PostgreSQL 17 with the local storage provider (criteria 1–5).
/// </summary>
public sealed class LeadDocumentsPostgreSqlTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public LeadDocumentsPostgreSqlTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static MultipartFormDataContent Form(string fileName, int size = 64, Guid? interactionId = null, string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(Enumerable.Repeat((byte)'x', size).ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        if (interactionId is { } id)
        {
            form.Add(new StringContent(id.ToString()), "leadInteractionId");
        }

        return form;
    }

    private async Task<(Guid CompanyId, HttpClient Client, Guid LeadId, Guid InteractionId, Guid NoAnswer)> LeadAsync()
    {
        var (companyId, client, _) = await CreateCompanyWithUserAsync(_factory);
        var catalogs = await DataAsync(await client.GetAsync("/api/v1/Leads/catalogs"));
        Guid Find(string list, string code) => catalogs.GetProperty(list).EnumerateArray().Single(i => i.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();
        var created = await client.PostAsJsonAsync("/api/v1/Leads", new
        {
            newPerson = new { personType = 2, firstName = "Docs", lastName = "SRL", commercialName = "Docs SRL" },
            topic = "Propuesta", relationshipTypeId = Find("relationshipTypes", "PROSPECT_CUSTOMER"), leadSourceId = Find("sources", "WEB"),
            firstInteraction = new { contactTypeCompanyId = Find("contactTypes", "EMAIL"), summary = "Enviada" }
        });
        var leadId = (await DataAsync(created)).GetProperty("lead").GetProperty("id").GetGuid();
        var interactionId = (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/interactions"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        return (companyId, client, leadId, interactionId, Find("reasons", "NO_ANSWER"));
    }

    [Fact]
    public async Task APdfOnAContact_IsStoredAndListed_UnderTheContactAndTheLead()
    {
        var (_, client, leadId, interactionId, _) = await LeadAsync();

        var upload = await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("Propuesta.pdf", interactionId: interactionId));
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var documentId = (await DataAsync(upload)).GetProperty("id").GetGuid();

        (await DataAsync(await client.GetAsync($"/api/v1/leads/{leadId}/documents?leadInteractionId={interactionId}"))).EnumerateArray()
            .Single().GetProperty("contactTypeName").GetString().Should().Be("Correo");
        (await DataAsync(await client.GetAsync($"/api/v1/leads/{leadId}/documents"))).GetArrayLength().Should().Be(1);
        (await DataAsync(await client.GetAsync($"/api/v1/Leads/{leadId}/timeline"))).EnumerateArray()
            .Should().Contain(t => t.GetProperty("kind").GetString() == "Document" && t.GetProperty("refId").GetGuid() == interactionId);

        var download = await client.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().HaveCount(64);

        await using var scope = _factory.Services.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LeadDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId);
        row.StorageProvider.Should().Be(StorageProviderKind.Local);
    }

    [Fact]
    public async Task EachLimit_AnswersItsCode()
    {
        var (_, client, leadId, interactionId, _) = await LeadAsync();
        (await client.PutAsJsonAsync("/api/v1/LeadAttachmentSettings", new { allowedDocumentTypes = 1, maxFileSizeBytes = 100, maxFilesPerInteraction = 1, maxFilesPerDay = 2 })).EnsureSuccessStatusCode();

        async Task Expect(HttpResponseMessage response, string code)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
            body.Should().Contain(code);
        }

        await Expect(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("foto.png", contentType: "image/png")), "LEAD_DOCUMENT_TYPE_NOT_ALLOWED");
        await Expect(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("grande.pdf", size: 101)), "LEAD_DOCUMENT_TOO_LARGE");
        await Expect(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("a.pdf", interactionId: Guid.NewGuid())), "INVALID_LEAD_INTERACTION");
        (await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("a.pdf", interactionId: interactionId))).StatusCode.Should().Be(HttpStatusCode.Created);
        await Expect(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("b.pdf", interactionId: interactionId)), "LEAD_DOCUMENT_LIMIT_PER_INTERACTION");
        (await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("c.pdf"))).StatusCode.Should().Be(HttpStatusCode.Created);
        await Expect(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("d.pdf")), "LEAD_DOCUMENT_LIMIT_PER_DAY");
    }

    [Fact]
    public async Task DownloadWithoutCanDownload_Is403_AndAnotherCompanysDocument_Is404()
    {
        var (companyId, client, leadId, _, _) = await LeadAsync();
        var documentId = (await DataAsync(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("a.pdf")))).GetProperty("id").GetGuid();

        await GrantAsync(_factory, companyId, "UsuarioSimple", "Leads", delete: false);
        var (member, _) = await AddUserAsync(_factory, companyId, "UsuarioSimple");
        (await member.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "the grant has no CanDownload");

        var (_, other, _) = await CreateCompanyWithUserAsync(_factory);
        (await other.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InactiveLead_DownloadsButDoesNotAccept_AndDeleteRestoreKeepsTheFile()
    {
        var (_, client, leadId, _, noAnswer) = await LeadAsync();
        var documentId = (await DataAsync(await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("a.pdf")))).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/v1/Leads/{leadId}/inactivate", new { leadReasonId = noAnswer })).EnsureSuccessStatusCode();

        (await client.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await client.PostAsync($"/api/v1/leads/{leadId}/documents", Form("b.pdf"));
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("LEAD_NOT_OPEN");

        (await client.DeleteAsync($"/api/v1/leads/{leadId}/documents/{documentId}")).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        string path;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            path = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().LeadDocuments.IgnoreQueryFilters().SingleAsync(d => d.Id == documentId)).Path;
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageResolver>().Current;
            await using var file = await storage.OpenReadAsync(path, CancellationToken.None);
            file.Should().NotBeNull("deleting is logical; the file stays");
        }

        (await client.PostAsync($"/api/v1/leads/{leadId}/documents/{documentId}/restore", null)).EnsureSuccessStatusCode();
        (await client.GetAsync($"/api/v1/leads/{leadId}/documents/{documentId}/download")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryCompany_HasDefaultAttachmentSettings()
    {
        var (_, client, _) = await CreateCompanyWithUserAsync(_factory);

        var settings = await DataAsync(await client.GetAsync("/api/v1/LeadAttachmentSettings"));

        settings.GetProperty("allowedDocumentTypeNames").EnumerateArray().Select(n => n.GetString()).Should().Equal("Pdf", "Word", "Excel", "Image");
        settings.GetProperty("maxFileSizeBytes").GetInt64().Should().Be(10L * 1024 * 1024);
        settings.GetProperty("maxFilesPerInteraction").GetInt32().Should().Be(5);
        settings.GetProperty("maxFilesPerDay").GetInt32().Should().Be(200);
        (await client.PostAsJsonAsync("/api/v1/LeadAttachmentSettings", new { allowedDocumentTypes = 1, maxFileSizeBytes = 1, maxFilesPerInteraction = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
