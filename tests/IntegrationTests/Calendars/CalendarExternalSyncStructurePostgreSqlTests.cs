using FluentAssertions;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Calendars;

/// <summary>
/// SPEC 46-post (PostgreSQL, <see cref="PostgreSqlWebApplicationFactory"/>) — verifies the structure of
/// the three new tables (<c>Calendar.ExternalConnections</c>, <c>Calendar.ExternalEventLinks</c>,
/// <c>Calendar.SyncOutbox</c>): they exist, their filtered unique indexes match the spec, and the
/// EF model compiles (the host built successfully, so the model is valid by construction).
/// </summary>
public sealed class CalendarExternalSyncStructurePostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private readonly PostgreSqlWebApplicationFactory _factory;

    public CalendarExternalSyncStructurePostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Schema_ShouldExposeAllThreeTablesAndFilteredIndexes()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Three tables present in the calendar schema (physical names are lower case, SPEC 39).
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'calendar' AND table_name IN ('externalconnections', 'externaleventlinks', 'syncoutbox')")
            .ToListAsync();
        tables.Should().BeEquivalentTo(["externalconnections", "externaleventlinks", "syncoutbox"]);

        // All five unique/filtered indexes from the spec, with their exact pg_indexes fragments.
        var indexDefs = (await db.Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'calendar' AND tablename = @t",
                new NpgsqlParameter("t", "externalconnections"))
            .ToListAsync())
            .Concat(await db.Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'calendar' AND tablename = @t",
                new NpgsqlParameter("t", "externaleventlinks"))
            .ToListAsync())
            .Concat(await db.Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'calendar' AND tablename = @t",
                new NpgsqlParameter("t", "syncoutbox"))
            .ToListAsync())
            .ToList();

        indexDefs.Should().Contain(d => d.Contains("(companyid, userid, provider)")
            && d.Contains("gcrecord = 0") && d.Contains("status <> 2"));
        indexDefs.Should().Contain(d => d.Contains("(watchchannelid)")
            && d.Contains("watchchannelid IS NOT NULL") && d.Contains("gcrecord = 0"));

        indexDefs.Should().Contain(d => d.Contains("(connectionid, calendareventid)")
            && d.Contains("gcrecord = 0") && !d.Contains("status = 1"));
        indexDefs.Should().Contain(d => d.Contains("(connectionid, externaleventid)")
            && d.Contains("gcrecord = 0") && !d.Contains("status = 1"));

        indexDefs.Should().Contain(d => d.Contains("(connectionid, calendareventid)")
            && d.Contains("status = 1") && d.Contains("gcrecord = 0"));
        indexDefs.Should().Contain(d => d.Contains("(status, nextattemptutc)")
            && d.Contains("gcrecord = 0"));
    }

    [Fact]
    public async Task Model_ShouldBuildWithoutErrors()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The host built successfully (fixture spins up the API), so the EF model is valid.
        // Confirm the three new DbSets are wired and queryable.
        db.CalendarExternalConnections.Should().NotBeNull();
        db.CalendarExternalEventLinks.Should().NotBeNull();
        db.CalendarSyncOutboxItems.Should().NotBeNull();

        // And the relational model exposes them in the model snapshot.
        var tables = db.Model.GetRelationalModel().Tables
            .Select(t => (Schema: t.Schema?.ToLowerInvariant(), Name: t.Name?.ToLowerInvariant()))
            .ToList();
        tables.Should().Contain(t => t.Schema == "calendar" && t.Name == "externalconnections");
        tables.Should().Contain(t => t.Schema == "calendar" && t.Name == "externaleventlinks");
        tables.Should().Contain(t => t.Schema == "calendar" && t.Name == "syncoutbox");
    }
}