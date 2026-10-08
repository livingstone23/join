// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Persistence;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 99 — internal channels <c>WEB</c>/<c>APP</c>: the data migration and the seed leave the six seeded
/// channels active without duplicates (clean and already-seeded database), the JOIN-001 ticket defaults use
/// <c>WEB</c>, and a deleted <c>WEB</c> is never revived by the seed (only a SuperAdmin restores it, SPEC 41).
/// </summary>
public sealed class CommunicationChannelSeedTests : IClassFixture<CustomWebApplicationFactory>
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

    private readonly CustomWebApplicationFactory _factory;

    public CommunicationChannelSeedTests(CustomWebApplicationFactory factory)
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
