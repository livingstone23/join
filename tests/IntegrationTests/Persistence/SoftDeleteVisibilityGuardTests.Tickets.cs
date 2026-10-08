// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Text.Json;
using FluentAssertions;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 41, Etapa 4 (tickets): the ticket listings join the visibility guard, plus the delete cascade of a
/// ticket, the follow-up block and the singleton configurations (one active row per company).
/// </summary>
public sealed partial class SoftDeleteVisibilityGuardTests
{
    private static IReadOnlyDictionary<string, VisibilityCase> WithTicketCases(Dictionary<string, VisibilityCase> cases)
    {
        cases["Ticket"] = new("Tickets", "Tickets", "search", true, async (db, tag, a, b) =>
        {
            var user = await SeedUserAsync(db);
            var catalogs = new Dictionary<Guid, TicketCatalogs> { [a] = await SeedTicketCatalogsAsync(db, a), [b] = await SeedTicketCatalogsAsync(db, b) };
            return await SeedTenantAsync(db, tag, (company, name) => NewTicket(company, catalogs[company], user, name), a, b);
        });
        cases["TicketDocument"] = new("TicketDocuments", "tickets/{key}/documents", string.Empty, true, async (db, _, a, b) =>
        {
            var user = await SeedUserAsync(db);
            var tickets = new Dictionary<Guid, Ticket>();
            foreach (var company in new[] { a, b })
            {
                var ticket = NewTicket(company, await SeedTicketCatalogsAsync(db, company), user, $"T{Guid.NewGuid():N}"[..12]);
                ticket.AddLog(user, LogType.Creation, "Created.");
                ticket.CreatedBy = Creator;
                db.Add(ticket);
                tickets[company] = ticket;
            }

            await db.SaveChangesAsync();
            var activeA = NewDocument(tickets[a]);
            var deletedA = NewDocument(tickets[a]);
            deletedA.GcRecord = DeletedStamp;
            var activeB = NewDocument(tickets[b]);
            db.AddRange(activeA, deletedA, activeB);
            await db.SaveChangesAsync();
            return new SeededRows(tickets[a].Id.ToString(), activeA.Id, deletedA.Id, activeB.Id, tickets[b].Id.ToString());
        });
        cases["TicketStatusTransition"] = new("TicketStatusTransitions", "TicketStatusTransitions", string.Empty, true, async (db, _, a, b) =>
        {
            var rows = new List<TicketStatusTransition>();
            foreach (var company in new[] { a, b, a })
            {
                var from = NewStatus(company);
                var to = NewStatus(company);
                db.AddRange(from, to);
                rows.Add(new TicketStatusTransition { CompanyId = company, FromStatusId = from.Id, ToStatusId = to.Id, CreatedBy = Creator });
            }

            rows[2].GcRecord = DeletedStamp;
            db.AddRange(rows);
            await db.SaveChangesAsync();
            return new SeededRows("-", rows[0].Id, rows[2].Id, rows[1].Id);
        });
        cases["TicketUserCompany"] = new("TicketUserCompanies", "TicketUserCompanies", string.Empty, true, async (db, _, a, b) =>
        {
            async Task<TicketUserCompany> MemberAsync(Guid company, int stamp)
            {
                // Restoring a roster entry requires the user to still be an active member of the company.
                var user = await SeedUserAsync(db);
                db.Add(new UserCompany { UserId = user, CompanyId = company, CreatedBy = Creator });
                var entry = new TicketUserCompany { UserId = user, CompanyId = company, CanResolveTicket = true, GcRecord = stamp, CreatedBy = Creator };
                db.Add(entry);
                return entry;
            }

            var activeA = await MemberAsync(a, 0);
            var deletedA = await MemberAsync(a, DeletedStamp);
            var activeB = await MemberAsync(b, 0);
            await db.SaveChangesAsync();
            return new SeededRows("-", activeA.Id, deletedA.Id, activeB.Id);
        });
        return cases;
    }

    [Fact]
    public async Task DeleteTicket_ShouldCascadeToItsAttachments_AndRestoreTicketShouldBringThemBackWithALog()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var (ticketId, documentId) = await SeedTicketWithDocumentAsync(companyA);
        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");

        var deleted = await superAdmin.DeleteAsync($"/api/v1/Tickets/{ticketId}");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK, await deleted.Content.ReadAsStringAsync());

        var (ticketStamp, documentStamp) = await ReadTicketStampsAsync(ticketId, documentId);
        ticketStamp.Should().BeGreaterThan(BaseAuditableEntity.ActiveGcRecord);
        documentStamp.Should().Be(ticketStamp, "SPEC 41 (Etapa 4): attachments share the cascade stamp of the ticket");

        var restored = await superAdmin.PostAsync($"/api/v1/Tickets/{ticketId}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());

        (await ReadTicketStampsAsync(ticketId, documentId)).Should().Be((BaseAuditableEntity.ActiveGcRecord, BaseAuditableEntity.ActiveGcRecord));

        var detail = await superAdmin.GetAsync($"/api/v1/Tickets/{ticketId}");
        var body = await detail.Content.ReadAsStringAsync();
        detail.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("Restoration", "restoring a ticket appends a TicketLog entry");
    }

    [Fact]
    public async Task DeleteTicket_WithAnActiveFollowUp_ShouldAnswerConflictAndDeleteNothing()
    {
        var (companyA, _) = await SeedCompaniesAsync();
        var (ticketId, documentId) = await SeedTicketWithDocumentAsync(companyA, withFollowUp: true);
        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");

        var response = await superAdmin.DeleteAsync($"/api/v1/Tickets/{ticketId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("TICKET_IN_USE").And.Contain("Active follow-up tickets: 1");
        (await ReadTicketStampsAsync(ticketId, documentId)).Should().Be((BaseAuditableEntity.ActiveGcRecord, BaseAuditableEntity.ActiveGcRecord));
    }

    [Theory]
    [InlineData("TicketCompanyDefaults")]
    [InlineData("TicketAttachmentSettings")]
    public async Task SingletonConfiguration_DeletedRowIsSuperAdminOnly_AndRestoreRespectsTheActiveOne(string resource)
    {
        var (companyA, _) = await SeedCompaniesAsync();
        Guid deletedId;
        Guid activeId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var deleted = NewConfiguration(resource, companyA);
            deleted.GcRecord = DeletedStamp;
            db.Add(deleted);
            await db.SaveChangesAsync();

            // A second active configuration after a deleted one: fails if the unique index on CompanyId is not
            // filtered by GcRecord (TicketCompanyDefaults before SPEC 41, Etapa 4).
            var active = NewConfiguration(resource, companyA);
            db.Add(active);
            await db.SaveChangesAsync();
            (deletedId, activeId) = (deleted.Id, active.Id);
        }

        using (var manager = await CreateClientAsync(companyA, "Manager"))
        {
            (await ConfigurationIdsAsync(manager, resource, includeDeleted: true)).Should().BeEquivalentTo(new[] { activeId });
        }

        using var superAdmin = await CreateClientAsync(companyA, "SuperAdmin");
        (await ConfigurationIdsAsync(superAdmin, resource, includeDeleted: false)).Should().BeEquivalentTo(new[] { activeId });
        (await ConfigurationIdsAsync(superAdmin, resource, includeDeleted: true)).Should().BeEquivalentTo(new[] { activeId, deletedId });

        var conflict = await superAdmin.PostAsync($"/api/v1/{resource}/{deletedId}/restore", content: null);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("ACTIVE_DUPLICATE_EXISTS");

        (await superAdmin.DeleteAsync($"/api/v1/{resource}/{activeId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = await superAdmin.PostAsync($"/api/v1/{resource}/{deletedId}/restore", content: null);
        restored.StatusCode.Should().Be(HttpStatusCode.OK, await restored.Content.ReadAsStringAsync());
        (await ConfigurationIdsAsync(superAdmin, resource, includeDeleted: false)).Should().BeEquivalentTo(new[] { deletedId });
    }

    private sealed record TicketCatalogs(Guid Status, Guid Complexity, Guid TimeUnit, Guid Channel);

    private static async Task<IReadOnlyList<Guid>> ConfigurationIdsAsync(HttpClient client, string resource, bool includeDeleted)
    {
        var response = await client.GetAsync($"/api/v1/{resource}" + (includeDeleted ? "?includeDeleted=true" : string.Empty));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToList();
    }

    private static BaseTenantEntity NewConfiguration(string resource, Guid company) => resource switch
    {
        "TicketCompanyDefaults" => new TicketCompanyDefault { CompanyId = company, StartCode = "TCK", CreatedBy = Creator },
        _ => new TicketAttachmentSettings
        {
            CompanyId = company,
            AllowedDocumentTypes = DocumentType.Pdf,
            MaxFileSizeBytes = 1024,
            MaxFilesPerTicket = 5,
            CreatedBy = Creator
        }
    };

    private async Task<(Guid TicketId, Guid DocumentId)> SeedTicketWithDocumentAsync(Guid companyId, bool withFollowUp = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await SeedUserAsync(db);
        var catalogs = await SeedTicketCatalogsAsync(db, companyId);
        var ticket = NewTicket(companyId, catalogs, user, $"T{Guid.NewGuid():N}"[..12]);
        ticket.CreatedBy = Creator;
        ticket.AddLog(user, LogType.Creation, "Created.");
        db.Add(ticket);
        await db.SaveChangesAsync();

        var document = NewDocument(ticket);
        db.Add(document);
        if (withFollowUp)
        {
            var followUp = NewTicket(companyId, catalogs, user, $"F{Guid.NewGuid():N}"[..12]);
            followUp.PrecedentTicketId = ticket.Id;
            followUp.CreatedBy = Creator;
            db.Add(followUp);
        }

        await db.SaveChangesAsync();
        return (ticket.Id, document.Id);
    }

    private async Task<(int Ticket, int Document)> ReadTicketStampsAsync(Guid ticketId, Guid documentId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ticket = await db.Set<Ticket>().IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == ticketId);
        var document = await db.Set<TicketDocument>().IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == documentId);
        return (ticket.GcRecord, document.GcRecord);
    }

    private static Ticket NewTicket(Guid company, TicketCatalogs catalogs, Guid user, string name)
    {
        var ticket = new Ticket
        {
            CompanyId = company,
            Name = name,
            Description = "Visibility guard ticket",
            TicketStatusId = catalogs.Status,
            TicketComplexityId = catalogs.Complexity,
            TimeUnitId = catalogs.TimeUnit,
            ChannelId = catalogs.Channel,
            CreatedByUserId = user
        };
        ticket.SetStandardCode(2026, 10, Random.Shared.Next(1, 999_999));
        return ticket;
    }

    private static TicketDocument NewDocument(Ticket ticket) => new()
    {
        CompanyId = ticket.CompanyId,
        TicketId = ticket.Id,
        TicketLogsId = ticket.TicketLogs.First().Id,
        DocumentType = DocumentType.Pdf,
        OriginalName = "file.pdf",
        NewName = $"{Guid.NewGuid():N}.pdf",
        Path = $"tickets/{Guid.NewGuid():N}.pdf",
        StorageProvider = StorageProviderKind.Local,
        ContentType = "application/pdf",
        SizeBytes = 10,
        CreatedBy = Creator
    };

    private static TicketStatus NewStatus(Guid company)
        => new() { CompanyId = company, Name = $"S{Guid.NewGuid():N}"[..20], Code = Random.Shared.Next(1000, 999_999), CreatedBy = Creator };

    private static async Task<TicketCatalogs> SeedTicketCatalogsAsync(ApplicationDbContext db, Guid company)
    {
        var status = NewStatus(company);
        var timeUnit = new TimeUnit { CompanyId = company, Name = $"U{Guid.NewGuid():N}"[..20], Code = 1, CreatedBy = Creator };
        var complexity = new TicketComplexity
        {
            CompanyId = company,
            Name = $"X{Guid.NewGuid():N}"[..20],
            Code = Random.Shared.Next(1000, 999_999),
            ResolutionTimeUnits = 8,
            TimeUnitId = timeUnit.Id,
            CreatedBy = Creator
        };
        var channel = new CommunicationChannel { Name = $"CH{Guid.NewGuid():N}"[..20], IsActive = true, CreatedBy = Creator };
        db.AddRange(status, timeUnit, complexity, channel);
        await db.SaveChangesAsync();
        return new TicketCatalogs(status.Id, complexity.Id, timeUnit.Id, channel.Id);
    }

    private static async Task<Guid> SeedUserAsync(ApplicationDbContext db)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = $"t{Guid.NewGuid():N}"[..12], Email = $"t{Guid.NewGuid():N}"[..12] + "@t.local", FirstName = "Ticket", LastName = "User" };
        user.NormalizedUserName = user.UserName!.ToUpperInvariant();
        user.NormalizedEmail = user.Email!.ToUpperInvariant();
        user.SecurityStamp = Guid.NewGuid().ToString();
        db.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
