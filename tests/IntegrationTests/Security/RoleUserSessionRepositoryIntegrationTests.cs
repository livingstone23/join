using FluentAssertions;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 30 (F4) — Integration coverage for <see cref="IRoleUserSessionRepository"/>.
///
/// Runs against a real SQL Server in Testcontainers via <see cref="CustomWebApplicationFactory"/>.
/// EF Core migrations (including the new <c>20260826000000_AddGcRecordToUserConnectionLogs</c>
/// one that brings <c>Security.UserConnectionLogs</c> up to soft-delete parity) are applied at
/// host boot. The repo uses Dapper against <see cref="ApplicationDbContext"/>'s underlying
/// connection; what these tests prove is that the hand-written SQL aligns with the actual
/// schema — exactly the gap the unit-test-only CI pipeline left open and the SQL 207 bug
/// slipped through.
///
/// Each test owns its seed (user + connection log + refresh token) and cleans up by stamping
/// <c>GcRecord</c> on rows it created rather than issuing <c>DELETE</c>s — preserves the
/// append-only / soft-delete contract the rest of the schema follows.
/// </summary>
[Collection("IntegrationTests")]
public sealed class RoleUserSessionRepositoryIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public RoleUserSessionRepositoryIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // ─── Setup helpers ────────────────────────────────────────────────────────────────

    private async Task<ApplicationUser> CreateUserAsync(IServiceProvider scopeServices, string suffix)
    {
        var userManager = scopeServices.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{suffix}@integration.test";
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = suffix,
            EmailConfirmed = true,
        };
        var result = await userManager.CreateAsync(user, "Str0ng!Pass2026");
        result.Succeeded.Should().BeTrue(string.Join(", ", result.Errors.Select(e => e.Description)));
        return user;
    }

    private async Task<UserConnectionLog> InsertConnectionLogAsync(
        ApplicationDbContext db,
        Guid userId,
        bool isActive = true,
        int gcRecord = 0)
    {
        var log = new UserConnectionLog
        {
            UserId = userId,
            IpAddress = "127.0.0.1",
            Country = "US",
            UserAgent = "integration-tests",
            ConnectionDate = DateTime.UtcNow,
            IsActiveSession = isActive,
            GcRecord = gcRecord,
            CreatedBy = nameof(RoleUserSessionRepositoryIntegrationTests),
        };
        db.UserConnectionLogs.Add(log);
        await db.SaveChangesAsync();
        return log;
    }

    private async Task<UserRefreshToken> InsertRefreshTokenAsync(
        ApplicationDbContext db,
        Guid userId,
        bool isRevoked = false,
        int gcRecord = 0,
        DateTime? expiry = null)
    {
        var token = new UserRefreshToken
        {
            UserId = userId,
            Token = $"rt-{Guid.NewGuid():N}",
            ExpiryDate = expiry ?? DateTime.UtcNow.AddDays(7),
            IsRevoked = isRevoked,
            GcRecord = gcRecord,
            CreatedBy = nameof(RoleUserSessionRepositoryIntegrationTests),
        };
        db.UserRefreshTokens.Add(token);
        await db.SaveChangesAsync();
        return token;
    }

    /// <summary>
    /// Stamps GcRecord on every row the test created so subsequent tests see a clean slate
    /// without resorting to physical DELETEs that would mask side effects.
    /// </summary>
    private async Task CleanupAsync(IServiceProvider scopeServices, params object[] entities)
    {
        var db = scopeServices.GetRequiredService<ApplicationDbContext>();
        var stamp = BaseAuditableEntity.GetDeletionGcRecordStamp();
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case UserConnectionLog log:
                    log.GcRecord = stamp;
                    break;
                case UserRefreshToken rt:
                    rt.GcRecord = stamp;
                    break;
                case ApplicationUser user:
                    await db.Entry(user).ReloadAsync();
                    user.GcRecord = stamp;
                    break;
            }
        }
        await db.SaveChangesAsync();
    }

    // ─── FindActiveByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task FindActiveByIdAsync_Matches_ActiveRefreshToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(FindActiveByIdAsync_Matches_ActiveRefreshToken));
        var token = await InsertRefreshTokenAsync(db, user.Id);

        try
        {
            var result = await repo.FindActiveByIdAsync(token.Id, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Id.Should().Be(token.Id);
            result.Type.Should().Be(SessionType.UserRefreshToken);
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, token, user);
        }
    }

    [Fact]
    public async Task FindActiveByIdAsync_Matches_ActiveConnectionLog()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(FindActiveByIdAsync_Matches_ActiveConnectionLog));
        var log = await InsertConnectionLogAsync(db, user.Id, isActive: true);

        try
        {
            var result = await repo.FindActiveByIdAsync(log.Id, CancellationToken.None);

            result.Should().NotBeNull();
            result!.Id.Should().Be(log.Id);
            result.Type.Should().Be(SessionType.UserConnectionLog);
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, log, user);
        }
    }

    [Fact]
    public async Task FindActiveByIdAsync_Returns_Null_When_OnlySoftDeleted()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(FindActiveByIdAsync_Returns_Null_When_OnlySoftDeleted));
        var deleted = await InsertRefreshTokenAsync(db, user.Id, gcRecord: BaseAuditableEntity.GetDeletionGcRecordStamp());
        var connectionDeleted = await InsertConnectionLogAsync(db, user.Id, isActive: false, gcRecord: BaseAuditableEntity.GetDeletionGcRecordStamp());

        try
        {
            var byToken = await repo.FindActiveByIdAsync(deleted.Id, CancellationToken.None);
            var byLog = await repo.FindActiveByIdAsync(connectionDeleted.Id, CancellationToken.None);

            byToken.Should().BeNull("GcRecord > 0 on UserRefreshTokens excludes the row");
            byLog.Should().BeNull("GcRecord > 0 on UserConnectionLogs excludes the row (F3 parity)");
        }
        finally
        {
            // No state to flip — already GcRecord-stamped at insert.
            await CleanupAsync(scope.ServiceProvider, user);
        }
    }

    [Fact]
    public async Task FindActiveByIdAsync_Returns_Null_For_MissingGuid()
    {
        using var scope = _factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var result = await repo.FindActiveByIdAsync(Guid.NewGuid(), CancellationToken.None);

        result.Should().BeNull();
    }

    // ─── GetUserIdBySessionIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserIdBySessionIdAsync_Returns_UserId_From_Each_Table()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(GetUserIdBySessionIdAsync_Returns_UserId_From_Each_Table));
        var token = await InsertRefreshTokenAsync(db, user.Id);
        var log = await InsertConnectionLogAsync(db, user.Id);

        try
        {
            var ownerFromToken = await repo.GetUserIdBySessionIdAsync(token.Id, CancellationToken.None);
            var ownerFromLog = await repo.GetUserIdBySessionIdAsync(log.Id, CancellationToken.None);

            ownerFromToken.Should().Be(user.Id);
            ownerFromLog.Should().Be(user.Id);
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, token, log, user);
        }
    }

    // ─── SoftRevokeSingleRefreshTokenAsync ────────────────────────────────────────────

    [Fact]
    public async Task SoftRevokeSingleRefreshTokenAsync_Revokes_One_Row()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(SoftRevokeSingleRefreshTokenAsync_Revokes_One_Row));
        var target = await InsertRefreshTokenAsync(db, user.Id);
        var other = await InsertRefreshTokenAsync(db, user.Id);

        try
        {
            var utcNow = DateTime.UtcNow;
            var affected = await repo.SoftRevokeSingleRefreshTokenAsync(target.Id, excludeCurrentRefreshTokenId: null, utcNow, CancellationToken.None);

            affected.Should().Be(1);

            await db.Entry(target).ReloadAsync();
            await db.Entry(other).ReloadAsync();
            target.IsRevoked.Should().BeTrue();
            other.IsRevoked.Should().BeFalse("`Id = @target` must not touch sibling tokens");
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, target, other, user);
        }
    }

    [Fact]
    public async Task SoftRevokeSingleRefreshTokenAsync_Is_Idempotent_On_AlreadyRevokedToken()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(SoftRevokeSingleRefreshTokenAsync_Is_Idempotent_On_AlreadyRevokedToken));
        var token = await InsertRefreshTokenAsync(db, user.Id, isRevoked: true);

        try
        {
            var first = await repo.SoftRevokeSingleRefreshTokenAsync(token.Id, excludeCurrentRefreshTokenId: null, DateTime.UtcNow, CancellationToken.None);

            first.Should().Be(0, "WHERE IsRevoked = 0 excludes already-revoked rows");
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, token, user);
        }
    }

    // ─── SoftRevokeSingleConnectionAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SoftRevokeSingleConnectionAsync_Revokes_Only_When_Active()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(SoftRevokeSingleConnectionAsync_Revokes_Only_When_Active));
        var active = await InsertConnectionLogAsync(db, user.Id, isActive: true);
        var alreadyClosed = await InsertConnectionLogAsync(db, user.Id, isActive: false);

        try
        {
            var affected = await repo.SoftRevokeSingleConnectionAsync(active.Id, DateTime.UtcNow, CancellationToken.None);

            affected.Should().Be(1);

            await db.Entry(active).ReloadAsync();
            await db.Entry(alreadyClosed).ReloadAsync();
            active.IsActiveSession.Should().BeFalse();
            alreadyClosed.IsActiveSession.Should().BeFalse("the second row was already inactive and must be untouched");
            active.DisconnectionDate.Should().NotBeNull();
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, active, alreadyClosed, user);
        }
    }

    // ─── SoftRevokeActiveConnectionsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task SoftRevokeActiveConnectionsAsync_Closes_All_Active()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(SoftRevokeActiveConnectionsAsync_Closes_All_Active));
        var first = await InsertConnectionLogAsync(db, user.Id, isActive: true);
        var second = await InsertConnectionLogAsync(db, user.Id, isActive: true);
        var inactive = await InsertConnectionLogAsync(db, user.Id, isActive: false);

        try
        {
            var affected = await repo.SoftRevokeActiveConnectionsAsync(user.Id, DateTime.UtcNow, CancellationToken.None);

            affected.Should().Be(2);

            await db.Entry(first).ReloadAsync();
            await db.Entry(second).ReloadAsync();
            await db.Entry(inactive).ReloadAsync();
            first.IsActiveSession.Should().BeFalse();
            second.IsActiveSession.Should().BeFalse();
            inactive.IsActiveSession.Should().BeFalse("the inactive row was already 0 — UPDATE unaffected it");
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, first, second, inactive, user);
        }
    }

    // ─── SoftRevokeRefreshTokensExceptAsync ───────────────────────────────────────────

    [Fact]
    public async Task SoftRevokeRefreshTokensExceptAsync_Closes_Others_Preserves_Current()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var repo = scope.ServiceProvider.GetRequiredService<IRoleUserSessionRepository>();

        var user = await CreateUserAsync(scope.ServiceProvider, nameof(SoftRevokeRefreshTokensExceptAsync_Closes_Others_Preserves_Current));
        var current = await InsertRefreshTokenAsync(db, user.Id);
        var neighbor = await InsertRefreshTokenAsync(db, user.Id);

        try
        {
            var affected = await repo.SoftRevokeRefreshTokensExceptAsync(user.Id, currentRefreshTokenId: current.Id, DateTime.UtcNow, CancellationToken.None);

            affected.Should().Be(1);

            await db.Entry(current).ReloadAsync();
            await db.Entry(neighbor).ReloadAsync();
            current.IsRevoked.Should().BeFalse("the current token must never be revoked by revoke-others");
            neighbor.IsRevoked.Should().BeTrue();
        }
        finally
        {
            await CleanupAsync(scope.ServiceProvider, current, neighbor, user);
        }
    }
}
