// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using JOIN.Persistence;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 99-post (PostgreSQL, <see cref="PostgreSqlWebApplicationFactory"/>) — internal channels <c>WEB</c>/<c>APP</c>: the data migration and the seed leave the six seeded
/// channels active without duplicates (clean and already-seeded database), the JOIN-001 ticket defaults use
/// <c>WEB</c>, and a deleted <c>WEB</c> is never revived by the seed (only a SuperAdmin restores it, SPEC 41).
/// </summary>
public sealed class CommunicationChannelSeedPostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private static readonly string[] SeededCodes =
    [
        CommunicationChannelCodes.SendGrid,
        CommunicationChannelCodes.Telegram,
        CommunicationChannelCodes.Twilio,
        CommunicationChannelCodes.WhatsApp,
        CommunicationChannelCodes.Web,
        CommunicationChannelCodes.App
    ];

    private readonly PostgreSqlWebApplicationFactory _factory;

    public CommunicationChannelSeedPostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_OnCleanAndOnSeededDatabase_ShouldKeepSixActiveChannelsAndDefaultTicketsToWeb()
    {
        _ = _factory.CreateClient(); // builds the host: migrations + full seed on the clean container database

        await AssertSeededChannelsAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var internalChannels = await db.CommunicationChannels.IgnoreQueryFilters()
                .Where(c => c.Code == CommunicationChannelCodes.Web || c.Code == CommunicationChannelCodes.App)
                .ToListAsync();
            internalChannels.Should().OnlyContain(c => c.CreatedBy == "Spec99_Migration",
                "the data migration inserts them before the seed runs, so an existing database receives them too");
        }

        await RunSeedAsync();

        await AssertSeededChannelsAsync();
    }

    [Fact]
    public async Task Seed_WhenWebIsDeleted_ShouldNotReviveItAndFallBack()
    {
        _ = _factory.CreateClient();
        await SetWebGcRecordAsync(20260101);

        try
        {
            var act = RunSeedAsync;
            await act.Should().NotThrowAsync();

            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var web = await db.CommunicationChannels.IgnoreQueryFilters().SingleAsync(c => c.Code == CommunicationChannelCodes.Web);
            web.GcRecord.Should().NotBe(BaseAuditableEntity.ActiveGcRecord, "the seed never restores a deleted channel");
            (await DefaultChannelCodeAsync(db)).Should().Be(CommunicationChannelCodes.WhatsApp, "a deleted WEB falls back to WHATSAPP");
        }
        finally
        {
            await SetWebGcRecordAsync(BaseAuditableEntity.ActiveGcRecord);
            await RunSeedAsync();
        }
    }

    /// <summary>
    /// SPEC 99-post (ajuste): the seed updates only the ACTIVE ticket configuration of JOIN-001 and never revives a
    /// deleted one. It used to pick any row and set <c>GcRecord = 0</c>: with a deleted row next to an active one,
    /// the revived row broke <c>ux_ticketcompanydefaults_company_active</c> and the host did not start.
    /// </summary>
    [Fact]
    public async Task Seed_WithDeletedTicketDefaults_ShouldNeverReviveThem()
    {
        _ = _factory.CreateClient();
        Guid companyId, activeId, deletedCopyId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var active = await JoinDefaults(db).SingleAsync(d => d.GcRecord == BaseAuditableEntity.ActiveGcRecord);
            companyId = active.CompanyId;
            activeId = active.Id;

            // A deleted copy inserted BEFORE the active row in physical order, so a query without a GcRecord
            // predicate would be likely to pick it.
            var deletedCopy = new TicketCompanyDefault
            {
                CompanyId = companyId,
                StartCode = active.StartCode,
                CodeSequenceLength = active.CodeSequenceLength,
                UsePersonalizedCode = active.UsePersonalizedCode,
                TicketStatusDefaultId = active.TicketStatusDefaultId,
                TicketComplexityDefaultId = active.TicketComplexityDefaultId,
                TimeUnitDefaultId = active.TimeUnitDefaultId,
                ChannelDefaultId = active.ChannelDefaultId,
                Created = active.Created.AddMinutes(-1),
                CreatedBy = nameof(CommunicationChannelSeedPostgreSqlTests),
                GcRecord = 20260101
            };
            db.TicketCompanyDefaults.Add(deletedCopy);
            await db.SaveChangesAsync();
            deletedCopyId = deletedCopy.Id;
        }

        try
        {
            // 1. Deleted row next to an active one: the active one is updated, the deleted one stays deleted.
            await FluentActions.Awaiting(RunSeedAsync).Should().NotThrowAsync();
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rows = await JoinDefaults(db).ToListAsync();
                rows.Single(r => r.Id == deletedCopyId).GcRecord.Should().NotBe(BaseAuditableEntity.ActiveGcRecord);
                rows.Where(r => r.GcRecord == BaseAuditableEntity.ActiveGcRecord).Select(r => r.Id).Should().Equal(activeId);
            }

            // 2. Only deleted rows: the seed creates a new active configuration and revives none.
            await SetDefaultsGcRecordAsync(activeId, 20260102);
            await FluentActions.Awaiting(RunSeedAsync).Should().NotThrowAsync();
            await using (var scope = _factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var rows = await JoinDefaults(db).ToListAsync();
                rows.Where(r => r.Id == activeId || r.Id == deletedCopyId)
                    .Should().OnlyContain(r => r.GcRecord != BaseAuditableEntity.ActiveGcRecord, "deleted rows are never revived");
                var created = rows.Should().ContainSingle(r => r.GcRecord == BaseAuditableEntity.ActiveGcRecord).Subject;
                created.Id.Should().NotBe(activeId);
                (await DefaultChannelCodeAsync(db)).Should().Be(CommunicationChannelCodes.Web);
            }
        }
        finally
        {
            // Leave a single active configuration (the original) for the other tests of this fixture.
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await JoinDefaults(db).ToListAsync();

            // Two saves: the filtered unique index is checked row by row, so the other rows are deleted first.
            foreach (var row in rows.Where(r => r.Id != activeId && r.GcRecord == BaseAuditableEntity.ActiveGcRecord))
            {
                row.GcRecord = 20260103;
            }

            await db.SaveChangesAsync();
            rows.Single(r => r.Id == activeId).GcRecord = BaseAuditableEntity.ActiveGcRecord;
            await db.SaveChangesAsync();
        }
    }

    private static IQueryable<TicketCompanyDefault> JoinDefaults(ApplicationDbContext db)
        => from defaults in db.TicketCompanyDefaults.IgnoreQueryFilters()
           join company in db.Companies.IgnoreQueryFilters() on defaults.CompanyId equals company.Id
           where company.TaxId == "JOIN-001"
           select defaults;

    private async Task SetDefaultsGcRecordAsync(Guid id, int gcRecord)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.TicketCompanyDefaults.IgnoreQueryFilters().SingleAsync(d => d.Id == id);
        row.GcRecord = gcRecord;
        await db.SaveChangesAsync();
    }

    private async Task AssertSeededChannelsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var channels = await db.CommunicationChannels.IgnoreQueryFilters()
            .Where(c => SeededCodes.Contains(c.Code!))
            .ToListAsync();

        channels.Select(c => c.Code).Should().BeEquivalentTo(SeededCodes, "each seeded channel exists exactly once");
        channels.Should().OnlyContain(c => c.GcRecord == BaseAuditableEntity.ActiveGcRecord && c.IsActive);
        channels.Where(c => c.Code is CommunicationChannelCodes.Web or CommunicationChannelCodes.App)
            .Should().OnlyContain(c => c.Provider == "Internal");

        (await DefaultChannelCodeAsync(db)).Should().Be(CommunicationChannelCodes.Web);
    }

    private static Task<string?> DefaultChannelCodeAsync(ApplicationDbContext db)
        => (from defaults in db.TicketCompanyDefaults.IgnoreQueryFilters()
            join company in db.Companies.IgnoreQueryFilters() on defaults.CompanyId equals company.Id
            join channel in db.CommunicationChannels.IgnoreQueryFilters() on defaults.ChannelDefaultId equals channel.Id
            where company.TaxId == "JOIN-001" && defaults.GcRecord == 0
            select channel.Code).SingleAsync();

    private async Task RunSeedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
    }

    private async Task SetWebGcRecordAsync(int gcRecord)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var web = await db.CommunicationChannels.IgnoreQueryFilters().SingleAsync(c => c.Code == CommunicationChannelCodes.Web);
        web.GcRecord = gcRecord;
        await db.SaveChangesAsync();
    }
}
