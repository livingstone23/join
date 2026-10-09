// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Globalization;
using FluentAssertions;
using Npgsql;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 40-post (D) — runtime behavior of the filtered unique indexes on a real PostgreSQL
/// (<see cref="PostgreSqlWebApplicationFactory"/>, <c>postgres:17</c>). Soft delete is reproduced with the
/// same stamp <c>MarkAsDeleted</c> writes (<c>yyyyMMdd</c> of today), so two deletions happen "the same day".
/// Every case runs in a transaction that is rolled back.
/// </summary>
public sealed class FilteredUniqueIndexesPostgreSqlTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private static readonly int TodayStamp =
        int.Parse(DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    private readonly PostgreSqlWebApplicationFactory _factory;

    public FilteredUniqueIndexesPostgreSqlTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>D.1 — create, delete, recreate and delete again the same contact on the same day.</summary>
    [Fact]
    public async Task PersonContact_DeletedTwiceTheSameDay_DoesNotCollide()
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var sourceId = await ScalarAsync<Guid>(connection, tx,
            "SELECT id FROM admin.personcontacts WHERE gcrecord = 0 ORDER BY id LIMIT 1");
        var value = $"spec40-{Guid.NewGuid():N}@test.local";
        var copySql = await BuildCopySqlAsync(connection, tx, "admin.personcontacts",
            new() { ["contactvalue"] = $"'{value}'" });

        var first = await ScalarAsync<Guid>(connection, tx, copySql + " RETURNING id", sourceId);
        await SoftDeleteAsync(connection, tx, "admin.personcontacts", first);

        var second = await ScalarAsync<Guid>(connection, tx, copySql + " RETURNING id", sourceId);
        var act = () => SoftDeleteAsync(connection, tx, "admin.personcontacts", second);

        await act.Should().NotThrowAsync("two contacts deleted the same day no longer share a unique key");
        await tx.RollbackAsync();
    }

    /// <summary>D.3 — two active genders with the same name in the same company are still rejected.</summary>
    [Fact]
    public async Task Gender_ActiveDuplicateNameInSameCompany_IsRejected()
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var sourceId = await ScalarAsync<Guid>(connection, tx,
            "SELECT id FROM admin.genders WHERE gcrecord = 0 ORDER BY id LIMIT 1");
        var copySql = await BuildCopySqlAsync(connection, tx, "admin.genders",
            new() { ["code"] = "left(md5(random()::text), 10)" });

        var violation = await FluentActions.Awaiting(() => ExecuteAsync(connection, tx, copySql, sourceId))
            .Should().ThrowAsync<PostgresException>();
        violation.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        violation.Which.ConstraintName.Should().Be("ux_genders_company_name");
        await tx.RollbackAsync();
    }

    /// <summary>D.4 — the same gender name in another company is allowed.</summary>
    [Fact]
    public async Task Gender_SameNameInAnotherCompany_IsAllowed()
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var sourceId = await ScalarAsync<Guid>(connection, tx,
            """
            SELECT g.id FROM admin.genders g
            WHERE g.gcrecord = 0
              AND EXISTS (SELECT 1 FROM common.companies c WHERE c.id <> g.companyid)
            ORDER BY g.id LIMIT 1
            """);
        var copySql = await BuildCopySqlAsync(connection, tx, "admin.genders", new()
        {
            ["companyid"] = "(SELECT c.id FROM common.companies c WHERE c.id <> t.companyid ORDER BY c.id LIMIT 1)"
        });

        (await ExecuteAsync(connection, tx, copySql, sourceId)).Should().Be(1);
        await tx.RollbackAsync();
    }

    /// <summary>D.5 — delete and recreate a region with the same name/country twice on the same day.</summary>
    [Fact]
    public async Task Region_DeletedTwiceTheSameDay_DoesNotCollide()
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var name = $"Spec40 {Guid.NewGuid():N}";
        for (var round = 0; round < 2; round++)
        {
            var id = await InsertRegionAsync(connection, tx, name, "S40");
            await SoftDeleteAsync(connection, tx, "admin.regions", id);
        }

        (await ScalarAsync<long>(connection, tx,
            $"SELECT count(*) FROM admin.regions WHERE name = '{name}' AND gcrecord = {TodayStamp}"))
            .Should().Be(2);
        await tx.RollbackAsync();
    }

    /// <summary>D.7 — two active regions of the same country without code are allowed ("code" IS NOT NULL filter).</summary>
    [Fact]
    public async Task Region_TwoActiveWithoutCode_AreAllowed()
    {
        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await InsertRegionAsync(connection, tx, $"Spec40 A {Guid.NewGuid():N}", code: null);
        var act = () => InsertRegionAsync(connection, tx, $"Spec40 B {Guid.NewGuid():N}", code: null);

        await act.Should().NotThrowAsync();
        await tx.RollbackAsync();
    }

    private static async Task<Guid> InsertRegionAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string name, string? code)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO admin.regions (id, name, code, countryid, created, gcrecord, companyid)
            SELECT gen_random_uuid(), @name, @code, co.id, NOW(), 0, c.id
            FROM common.countries co CROSS JOIN common.companies c
            ORDER BY co.id, c.id LIMIT 1
            RETURNING id
            """, connection, tx);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("code", (object?)code ?? DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private static async Task SoftDeleteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string table, Guid id)
        => await ExecuteAsync(connection, tx, $"UPDATE {table} SET gcrecord = {TodayStamp} WHERE id = @id", id);

    /// <summary>
    /// <c>INSERT ... SELECT</c> that copies the row <c>@id</c> of <paramref name="table"/> with a new id and the
    /// given column expressions (evaluated against the source row aliased <c>t</c>).
    /// </summary>
    private static async Task<string> BuildCopySqlAsync(
        NpgsqlConnection connection, NpgsqlTransaction tx, string table, Dictionary<string, string> overrides)
    {
        var parts = table.Split('.');
        var columns = new List<string>();
        await using (var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = @s AND table_name = @t ORDER BY ordinal_position",
            connection, tx))
        {
            command.Parameters.AddWithValue("s", parts[0]);
            command.Parameters.AddWithValue("t", parts[1]);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        var selectList = columns.Select(c =>
            c == "id" ? "gen_random_uuid()"
            : overrides.TryGetValue(c, out var expression) ? expression
            : $"t.{c}");

        return $"INSERT INTO {table} ({string.Join(", ", columns)}) " +
               $"SELECT {string.Join(", ", selectList)} FROM {table} t WHERE t.id = @id";
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        _ = _factory.Services; // host startup = migrate + seed
        var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, Guid? id = null)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        if (id.HasValue)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }

        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync();
    }
}
