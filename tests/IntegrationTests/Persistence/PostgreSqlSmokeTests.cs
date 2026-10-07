// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 39 (F6) — smoke suite for the main_postgresql fork, against a real PostgreSQL
/// (Testcontainers in <see cref="PostgreSqlWebApplicationFactory"/>). Not full parity with the
/// SQL Server suite: it covers the highest-risk points of the port — the InitialPostgres
/// migration, the seed, the 11 filtered unique indexes, one paged CRUD flow and the session
/// flow whose <c>TOP (n)</c> queries were rewritten to <c>LIMIT</c>.
/// CI runs it with <c>--filter "FullyQualifiedName~PostgreSql"</c>.
/// </summary>
public sealed class PostgreSqlSmokeTests : IClassFixture<PostgreSqlWebApplicationFactory>
{
    private const string Password = "Smoke!Pass2026";

    private readonly PostgreSqlWebApplicationFactory _factory;

    public PostgreSqlSmokeTests(PostgreSqlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Case 1 — <c>InitialPostgres</c> applies cleanly on an empty PostgreSQL: it is the only
    /// migration in the history and nothing is left pending after host startup.
    /// </summary>
    [Fact]
    public async Task Migration_InitialPostgres_IsTheOnlyAppliedMigration()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Database.IsNpgsql().Should().BeTrue();
        (await db.Database.GetAppliedMigrationsAsync()).Should().ContainSingle()
            .Which.Should().EndWith("_InitialPostgres");
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    /// <summary>
    /// Case 2 — <c>DatabaseSeeder</c> ran to completion at startup: every catalog the
    /// remaining cases depend on has rows.
    /// </summary>
    [Theory]
    [InlineData("common.companies")]
    [InlineData("common.countries")]
    [InlineData("common.communicationchannels")]
    [InlineData("security.users")]
    [InlineData("security.roles")]
    [InlineData("security.systemoptions")]
    [InlineData("security.rolesystemoptions")]
    [InlineData("security.usercompanies")]
    [InlineData("messaging.ticketstatuses")]
    [InlineData("messaging.ticketcomplexities")]
    [InlineData("messaging.timeunits")]
    [InlineData("messaging.tickets")]
    public async Task Seed_PopulatesCatalog(string table)
    {
        _ = _factory.Services; // host startup = migrate + seed

        await using var connection = await OpenConnectionAsync();
        var count = await ScalarAsync<long>(connection, null, $"SELECT COUNT(*) FROM {table}");

        count.Should().BePositive($"DatabaseSeeder must populate {table}");
    }

    /// <summary>
    /// Case 3 — each of the 11 filtered unique indexes behaves at runtime as on SQL Server:
    /// a second active row with the same key is rejected by THAT index (not another
    /// constraint), and once the first row is soft-deleted (<c>gcrecord &lt;&gt; 0</c>) the same
    /// key can be inserted again. Every case runs in a transaction that is rolled back.
    /// </summary>
    [Theory]
    [MemberData(nameof(FilteredIndexCases))]
    public async Task FilteredUniqueIndex_RejectsActiveDuplicate_AndAllowsItAfterSoftDelete(FilteredIndexCase testCase)
    {
        _ = _factory.Services;

        await using var connection = await OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var sourceId = await ScalarAsync<Guid>(connection, tx, testCase.SourceSql);
        var duplicateSql = await BuildDuplicateSqlAsync(connection, tx, testCase);

        await using (var savepoint = new NpgsqlCommand("SAVEPOINT dup", connection, tx))
        {
            await savepoint.ExecuteNonQueryAsync();
        }

        var violation = await FluentActions
            .Awaiting(() => ExecuteAsync(connection, tx, duplicateSql, sourceId))
            .Should().ThrowAsync<PostgresException>();
        violation.Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
        violation.Which.ConstraintName.Should().Be(testCase.IndexName);

        await using (var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT dup", connection, tx))
        {
            await rollback.ExecuteNonQueryAsync();
        }

        await ExecuteAsync(connection, tx, $"UPDATE {testCase.Table} SET gcrecord = 1 WHERE id = @id", sourceId);
        (await ExecuteAsync(connection, tx, duplicateSql, sourceId))
            .Should().Be(1, "a soft-deleted row must not block re-creating the same key");

        await tx.RollbackAsync();
    }

    /// <summary>
    /// Case 4 — Tickets end to end over HTTP: create, list paged (LIMIT/OFFSET), filter by
    /// search and by status.
    /// </summary>
    [Fact]
    public async Task Tickets_CreateListPagedAndFilter_WorksEndToEnd()
    {
        _ = _factory.Services;

        Guid companyId, statusId, complexityId, timeUnitId, channelId;
        await using (var connection = await OpenConnectionAsync())
        {
            companyId = await ScalarAsync<Guid>(connection, null,
                "SELECT companyid FROM messaging.ticketstatuses WHERE isinitial = TRUE AND gcrecord = 0 ORDER BY companyid LIMIT 1");
            statusId = await ScalarAsync<Guid>(connection, null,
                $"SELECT id FROM messaging.ticketstatuses WHERE companyid = '{companyId}' AND isinitial = TRUE AND gcrecord = 0");
            complexityId = await ScalarAsync<Guid>(connection, null,
                $"SELECT id FROM messaging.ticketcomplexities WHERE companyid = '{companyId}' AND gcrecord = 0 ORDER BY name LIMIT 1");
            timeUnitId = await ScalarAsync<Guid>(connection, null,
                $"SELECT id FROM messaging.timeunits WHERE companyid = '{companyId}' AND gcrecord = 0 ORDER BY name LIMIT 1");
            channelId = await ScalarAsync<Guid>(connection, null,
                "SELECT id FROM common.communicationchannels WHERE gcrecord = 0 ORDER BY name LIMIT 1");
        }

        var (client, _) = await CreateAuthenticatedClientAsync(companyId);
        using var _client = client;

        var token = $"Smoke{Guid.NewGuid():N}";
        var createdIds = new List<Guid>();
        for (var i = 1; i <= 3; i++)
        {
            var createResponse = await client.PostAsJsonAsync("/api/v1/tickets", new CreateTicketDto
            {
                Name = $"{token} ticket {i}",
                Description = "PostgreSQL smoke test ticket",
                EstimatedTime = 2,
                ConsumedTime = 0,
                TicketStatusId = statusId,
                TicketComplexityId = complexityId,
                TimeUnitId = timeUnitId,
                ChannelId = channelId
            });
            createResponse.StatusCode.Should().Be(HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());
            createdIds.Add((await createResponse.Content.ReadFromJsonAsync<Response<TicketDto>>())!.Data!.Id);
        }

        // Paging is scoped to this test's own tickets (search) so it does not depend on the seed.
        var page1 = await GetTicketsAsync(client, $"search={token}&pageNumber=1&pageSize=2");
        var page2 = await GetTicketsAsync(client, $"search={token}&pageNumber=2&pageSize=2");
        page1.TotalCount.Should().Be(3);
        page1.Items.Should().HaveCount(2);
        page2.Items.Should().ContainSingle();
        page1.Items.Concat(page2.Items).Select(t => t.Id).Should().BeEquivalentTo(createdIds);

        var byName = await GetTicketsAsync(client, $"search={token} ticket 2");
        byName.Items.Should().ContainSingle().Which.Id.Should().Be(createdIds[1]);

        var byStatus = await GetTicketsAsync(client, $"search={token}&ticketStatusId={statusId}");
        byStatus.TotalCount.Should().Be(3);
    }

    /// <summary>
    /// Case 5 — session/login flow over the rewritten <c>TOP (n)</c> → <c>LIMIT</c> queries:
    /// login, list own sessions, resolve and revoke a sibling session
    /// (<see cref="IRoleUserSessionRepository"/>), and read back the most recent OTP codes
    /// (<see cref="IEmailOtpEnableCodeRepository"/>, <see cref="IPhoneVerificationCodeRepository"/>).
    /// </summary>
    [Fact]
    public async Task Sessions_LoginListAndRevoke_UseRewrittenLimitQueries()
    {
        _ = _factory.Services;

        Guid companyId;
        await using (var connection = await OpenConnectionAsync())
        {
            companyId = await ScalarAsync<Guid>(connection, null,
                "SELECT id FROM common.companies WHERE gcrecord = 0 ORDER BY created LIMIT 1");
        }

        var (client, userId) = await CreateAuthenticatedClientAsync(companyId);
        using var _client = client;

        var sessionsResponse = await client.GetAsync("/api/v1/account/sessions");
        sessionsResponse.StatusCode.Should().Be(HttpStatusCode.OK, await sessionsResponse.Content.ReadAsStringAsync());
        using (var sessions = JsonDocument.Parse(await sessionsResponse.Content.ReadAsStringAsync()))
        {
            sessions.RootElement.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
            sessions.RootElement.GetProperty("data").GetArrayLength().Should().BePositive();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sessionRepository = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var sibling = new UserRefreshToken
        {
            UserId = userId,
            Token = $"rt-smoke-{Guid.NewGuid():N}",
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(PostgreSqlSmokeTests),
        };
        db.UserRefreshTokens.Add(sibling);
        await db.SaveChangesAsync();

        (await sessionRepository.FindActiveByIdAsync(sibling.Id, CancellationToken.None))
            .Should().Be(new SessionLookupResult(sibling.Id, SessionType.UserRefreshToken));
        (await sessionRepository.GetUserIdBySessionIdAsync(sibling.Id, CancellationToken.None))
            .Should().Be(userId);

        var revokeResponse = await client.DeleteAsync($"/api/v1/account/sessions/{sibling.Id}");
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK, await revokeResponse.Content.ReadAsStringAsync());
        var revoked = await revokeResponse.Content.ReadFromJsonAsync<Response<RevokeMySessionResponseDto>>();
        revoked!.Data!.RevokedTokens.Should().Be(1);

        (await sessionRepository.FindActiveByIdAsync(sibling.Id, CancellationToken.None))
            .Should().BeNull("a revoked refresh token is no longer an active session");

        var now = DateTime.UtcNow;
        var emailOtpRepository = scope.ServiceProvider.GetRequiredService<IEmailOtpEnableCodeRepository>();
        var olderEmailCode = NewEmailOtp(userId, now.AddMinutes(5));
        var newerEmailCode = NewEmailOtp(userId, now.AddMinutes(10));
        await emailOtpRepository.InsertAsync(olderEmailCode, CancellationToken.None);
        await emailOtpRepository.InsertAsync(newerEmailCode, CancellationToken.None);
        (await emailOtpRepository.GetLatestActiveByUserAsync(userId, now, CancellationToken.None))!
            .Id.Should().Be(newerEmailCode.Id);

        var phoneRepository = scope.ServiceProvider.GetRequiredService<IPhoneVerificationCodeRepository>();
        var olderPhoneCode = NewPhoneCode(userId, now.AddMinutes(5));
        var newerPhoneCode = NewPhoneCode(userId, now.AddMinutes(10));
        await phoneRepository.InsertAsync(olderPhoneCode, CancellationToken.None);
        await phoneRepository.InsertAsync(newerPhoneCode, CancellationToken.None);
        (await phoneRepository.GetLatestActiveByUserAsync(userId, now, CancellationToken.None))!
            .Id.Should().Be(newerPhoneCode.Id);
    }

    /// <summary>
    /// Ajuste por SPEC 39 (F8.4) — a unique violation surfaces as PostgreSQL SQLSTATE 23505;
    /// <c>GlobalExceptionHandler</c> must map it to 409 <c>DUPLICATE_KEY</c>, as it does for
    /// SQL Server's 2601/2627, instead of a generic 500. Exercised through the filtered index
    /// that allows a single initial ticket status per company.
    /// </summary>
    [Fact]
    public async Task DuplicateKey_ViaFilteredIndex_ReturnsConflict()
    {
        _ = _factory.Services;

        Guid companyId;
        await using (var connection = await OpenConnectionAsync())
        {
            companyId = await ScalarAsync<Guid>(connection, null,
                "SELECT companyid FROM messaging.ticketstatuses WHERE isinitial = TRUE AND gcrecord = 0 ORDER BY companyid LIMIT 1");
        }

        var (client, _) = await CreateAuthenticatedClientAsync(companyId);
        using var _client = client;

        var response = await client.PostAsJsonAsync("/api/v1/ticketstatuses", new
        {
            Name = $"Smoke initial {Guid.NewGuid():N}",
            Code = 900 + Random.Shared.Next(99),
            IsActive = true,
            IsInitial = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("DUPLICATE_KEY");
    }

    /// <summary>
    /// The 11 filtered unique indexes of SPEC 39 Scope D (8 listed + 3 added by later specs).
    /// <see cref="FilteredIndexCase.SourceSql"/> returns the id of an active row to duplicate —
    /// an existing seeded row, or one inserted in-transaction for tables the seed leaves empty.
    /// <see cref="FilteredIndexCase.Overrides"/> replaces columns of the duplicate (alias
    /// <c>t</c> = source row) so it only collides on the filtered index under test.
    /// </summary>
    public static TheoryData<FilteredIndexCase> FilteredIndexCases => new()
    {
        new("ux_usercompanies_userid_default", "security.usercompanies",
            "SELECT id FROM security.usercompanies WHERE isdefault = TRUE AND gcrecord = 0 ORDER BY id LIMIT 1",
            // A different company, so the unfiltered (userid, companyid) index does not fire.
            new() { ["companyid"] = "(SELECT c.id FROM common.companies c WHERE c.id <> t.companyid ORDER BY c.id LIMIT 1)" }),
        new("ix_rolecompanies_roleid_companyid", "security.rolecompanies",
            """
            INSERT INTO security.rolecompanies (id, roleid, created, gcrecord, companyid)
            SELECT gen_random_uuid(), r.id, NOW(), 0, c.id
            FROM security.roles r CROSS JOIN common.companies c
            ORDER BY r.id, c.id LIMIT 1
            RETURNING id
            """),
        new("ix_usercommunicationchannels_companyid_userid_communicationcha~", "admin.usercommunicationchannels",
            """
            INSERT INTO admin.usercommunicationchannels
                (id, userid, communicationchannelid, channelidentifier, ispreferred, created, gcrecord, companyid)
            SELECT gen_random_uuid(), u.id, ch.id, 'smoke@test.local', FALSE, NOW(), 0, c.id
            FROM security.users u CROSS JOIN common.communicationchannels ch CROSS JOIN common.companies c
            ORDER BY u.id, ch.id, c.id LIMIT 1
            RETURNING id
            """),
        new("ux_ticketstatustransitions_company_from_to", "messaging.ticketstatustransitions",
            """
            INSERT INTO messaging.ticketstatustransitions (id, fromstatusid, tostatusid, created, gcrecord, companyid)
            SELECT gen_random_uuid(), a.id, b.id, NOW(), 0, a.companyid
            FROM messaging.ticketstatuses a
            JOIN messaging.ticketstatuses b ON b.companyid = a.companyid AND b.id <> a.id
            ORDER BY a.id, b.id LIMIT 1
            RETURNING id
            """),
        new("ux_ticketusercompanies_user_company", "messaging.ticketusercompanies",
            "SELECT id FROM messaging.ticketusercompanies WHERE gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_ticketattachmentsettings_company_active", "messaging.ticketattachmentsettings",
            "SELECT id FROM messaging.ticketattachmentsettings WHERE gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_ticketcomplexities_company_name", "messaging.ticketcomplexities",
            "SELECT id FROM messaging.ticketcomplexities WHERE gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_timeunits_company_name", "messaging.timeunits",
            "SELECT id FROM messaging.timeunits WHERE gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_ticketstatuses_company_initial", "messaging.ticketstatuses",
            "SELECT id FROM messaging.ticketstatuses WHERE isinitial = TRUE AND gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_ticketstatuses_company_paused", "messaging.ticketstatuses",
            "SELECT id FROM messaging.ticketstatuses WHERE ispaused = TRUE AND gcrecord = 0 ORDER BY id LIMIT 1"),
        new("ux_ticketstatuses_company_final", "messaging.ticketstatuses",
            "SELECT id FROM messaging.ticketstatuses WHERE isfinal = TRUE AND gcrecord = 0 ORDER BY id LIMIT 1"),
    };

    /// <summary>One filtered-index scenario; <see cref="ToString"/> names the xUnit case.</summary>
    public sealed record FilteredIndexCase(
        string IndexName,
        string Table,
        string SourceSql,
        Dictionary<string, string>? Overrides = null)
    {
        public override string ToString() => IndexName;
    }

    /// <summary>
    /// Builds <c>INSERT INTO table (cols) SELECT ... FROM table t WHERE t.id = @id</c> copying
    /// every column of the source row except a fresh <c>id</c> and the case's overrides.
    /// </summary>
    private static async Task<string> BuildDuplicateSqlAsync(NpgsqlConnection connection, NpgsqlTransaction tx, FilteredIndexCase testCase)
    {
        var parts = testCase.Table.Split('.');
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

        var overrides = testCase.Overrides ?? new Dictionary<string, string>();
        var selectList = columns.Select(c =>
            c == "id" ? "gen_random_uuid()"
            : overrides.TryGetValue(c, out var expression) ? expression
            : $"t.{c}");

        return $"INSERT INTO {testCase.Table} ({string.Join(", ", columns)}) " +
               $"SELECT {string.Join(", ", selectList)} FROM {testCase.Table} t WHERE t.id = @id";
    }

    private async Task<(HttpClient Client, Guid UserId)> CreateAuthenticatedClientAsync(Guid companyId)
    {
        var email = $"pg{Guid.NewGuid():N}@smoke.local";

        using var unauthClient = _factory.CreateClient();
        var registerResponse = await unauthClient.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand
        {
            Email = email,
            Password = Password,
            FirstName = "Integration",
            LastName = "Tester",
        });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK, await registerResponse.Content.ReadAsStringAsync());

        Guid userId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = await userManager.FindByEmailAsync(email);
            user.Should().NotBeNull();
            userId = user!.Id;

            seedDb.UserCompanies.Add(new UserCompany
            {
                UserId = userId,
                CompanyId = companyId,
                IsDefault = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(PostgreSqlSmokeTests)
            });
            (await userManager.AddToRoleAsync(user, "SuperAdmin")).Succeeded.Should().BeTrue();
            await seedDb.SaveChangesAsync();
        }

        var loginResponse = await unauthClient.PostAsJsonAsync("/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK, await loginResponse.Content.ReadAsStringAsync());
        var login = await loginResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Data!.Token);
        client.DefaultRequestHeaders.Add("X-Company-Id", companyId.ToString());
        return (client, userId);
    }

    private static async Task<PagedResult<TicketListItemDto>> GetTicketsAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/tickets?{query}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<Response<PagedResult<TicketListItemDto>>>();
        body!.IsSuccess.Should().BeTrue();
        return body.Data!;
    }

    private static EmailOtpEnableCode NewEmailOtp(Guid userId, DateTime expiresAtUtc) => new()
    {
        UserId = userId,
        Email = "smoke@test.local",
        CodeHash = "smoke-hash",
        ExpiresAtUtc = expiresAtUtc,
        GcRecord = BaseAuditableEntity.ActiveGcRecord,
        CreatedBy = nameof(PostgreSqlSmokeTests),
    };

    private static PhoneVerificationCode NewPhoneCode(Guid userId, DateTime expiresAtUtc) => new()
    {
        UserId = userId,
        PhoneNumber = "+50400000000",
        CodeHash = "smoke-hash",
        ExpiresAtUtc = expiresAtUtc,
        GcRecord = BaseAuditableEntity.ActiveGcRecord,
        CreatedBy = nameof(PostgreSqlSmokeTests),
    };

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, NpgsqlTransaction? tx, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        var value = await command.ExecuteScalarAsync();
        value.Should().NotBeNull($"query must return a row: {sql}");
        return (T)value!;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction tx, string sql, Guid id)
    {
        await using var command = new NpgsqlCommand(sql, connection, tx);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync();
    }
}
