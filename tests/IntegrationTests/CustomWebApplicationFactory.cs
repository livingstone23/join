using System.Collections.Concurrent;
using JOIN.Application.Interface;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Testcontainers.MsSql;

namespace JOIN.IntegrationTests;

/// <summary>
/// WebApplicationFactory base for integration tests. Spins up an ephemeral SQL Server
/// via Testcontainers before the host boots, points the host at it via the
/// <c>ConnectionStrings:DefaultConnection</c> override, and replaces the real
/// <see cref="IEmailService"/> with a mock so no test depends on an external
/// network or sends real emails. EF Core migrations run automatically at startup
/// — see <c>Program.cs</c>, no extra migration logic lives here on purpose.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Must be set BEFORE any host in this process builds — see the static constructor below.
    // RateLimitingServiceCollectionExtensions.AddJoinRateLimiting reads
    // configuration.GetSection("RateLimiting") EAGERLY (a one-time `section.Get<...>()`
    // snapshot captured into the AddPolicy("Strict", ...) closure) rather than lazily via
    // IOptionsSnapshot/IOptionsMonitor — unlike ConnectionStrings:DefaultConnection, which is
    // deliberately resolved lazily inside the AddDbContext callback (see
    // 3.Persistence/Configuration/ConfigureServices.cs) specifically so a
    // ConfigureWebHost/ConfigureAppConfiguration overlay can still reach it. Because of that
    // eager read, overriding "RateLimiting:Strict:PermitLimit" via ConfigureAppConfiguration's
    // in-memory collection does NOT reliably reach AddJoinRateLimiting before it runs — it
    // depends on exactly when that overlay gets spliced into builder.Configuration relative to
    // Program.cs's own top-level statements, which is not guaranteed for an eagerly-read value.
    // Environment variables, by contrast, are added as one of the very first configuration
    // providers inside WebApplication.CreateBuilder(args) itself, before ANY of Program.cs's
    // subsequent code (including AddJoinRateLimiting) executes — so setting them here, before
    // this in-process TestServer's host is ever built, is what actually works.
    static CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", "1000000");
        Environment.SetEnvironmentVariable("RateLimiting__Strict__PermitLimit", "1000000");
    }

    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private string _connectionString = string.Empty;

    // Process-wide lock around host build. Serilog's <c>Log.Logger</c> is a static singleton;
    // <see cref="WebApplicationFactory{TEntryPoint}"/> instances created concurrently by xUnit
    // (one per <c>[Collection]</c> test class via <c>IClassFixture</c>) each invoke
    // <c>Program.Main</c>, which assigns a fresh bootstrap logger to <c>Log.Logger</c> and then
    // calls <c>UseSerilog(...)</c>. Without serialization, factory B overwrites the bootstrap
    // assignment before factory A's <c>ILoggerFactory</c> resolution freezes it — A's freeze
    // then collides with B's later freeze of the same instance:
    // <c>InvalidOperationException: The logger is already frozen.</c>
    private static readonly object _hostBuildLock = new();

    /// <summary>
    /// Resets <see cref="Log.Logger"/> to a fresh empty instance before each host build so
    /// the bootstrap logger assignment in <c>Program.cs</c> runs cleanly and Serilog's
    /// <c>UseSerilog</c> can <c>Freeze()</c> a fresh instance. The reset + base build are
    /// wrapped in a static lock so concurrent <see cref="CustomWebApplicationFactory"/>
    /// instances cannot race on the shared <c>Log.Logger</c> singleton.
    /// <para>
    /// Cannot pass <c>null</c> to <see cref="Log.Logger"/> — its setter rejects null via
    /// <c>Guard.AgainstNull</c>. A no-sink <see cref="LoggerConfiguration"/> is the cheapest
    /// valid placeholder; <c>Program.cs</c> overwrites it with the real bootstrap logger on
    /// its first line, so no log output is lost.
    /// </para>
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (_hostBuildLock)
        {
            Log.Logger = new LoggerConfiguration().CreateLogger();
            return base.CreateHost(builder);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            // CapturingEmailService itself is Singleton (one shared capture store per factory
            // instance, resolvable from tests via Services.GetRequiredService<CapturingEmailService>()
            // to read back OTP codes emailed during SPEC 32's MFA flows) but is exposed as
            // IEmailService via a Transient factory delegate — matching the production lifetime
            // (services.AddTransient<IEmailService, SendGridEmailAdapter>() in
            // JOIN.Infrastructure.DependencyInjection) so Singleton consumers like
            // HealthCheckEmailPublisher don't hit DI scope validation.
            services.AddSingleton<CapturingEmailService>();
            services.AddTransient<IEmailService>(sp => sp.GetRequiredService<CapturingEmailService>());
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        var mappedPort = _dbContainer.GetMappedPublicPort(1433);
        Console.WriteLine($"[DIAG] Testcontainers hostname={_dbContainer.Hostname} mappedPort={mappedPort}");

        var builder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            ConnectTimeout = 30,
            IPAddressPreference = SqlConnectionIPAddressPreference.IPv4First
        };
        _connectionString = builder.ConnectionString;

        await WaitUntilStablyQueryableAsync();
    }

    // The official mssql-server image can briefly accept connections right after
    // Testcontainers' own "docker exec sqlcmd" readiness check succeeds, then go
    // dark for minutes while the engine finishes first-boot setup (service master
    // key / certificate generation, possible internal restart). CI evidence: our
    // own probe succeeded on the very first attempt, then every connection attempt
    // from the app's own startup retry loop — using a fresh DbContext/connection
    // each time — failed for the next ~2.5 minutes straight. A single successful
    // SELECT 1 is therefore not a reliable readiness signal for this image; require
    // several CONSECUTIVE successes, spaced apart, before declaring the container
    // ready, so the host is never started against a server that's about to
    // disappear.
    private async Task WaitUntilStablyQueryableAsync()
    {
        const int requiredConsecutiveSuccesses = 3;
        const int maxAttempts = 60;
        var delay = TimeSpan.FromSeconds(5);

        var consecutiveSuccesses = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync();

                consecutiveSuccesses++;
                Console.WriteLine($"[DIAG] stability check succeeded ({consecutiveSuccesses}/{requiredConsecutiveSuccesses}) on attempt {attempt}/{maxAttempts}");

                if (consecutiveSuccesses >= requiredConsecutiveSuccesses)
                {
                    return;
                }
            }
            catch (SqlException ex)
            {
                Console.WriteLine(consecutiveSuccesses > 0
                    ? $"[DIAG] stability check regressed after {consecutiveSuccesses} consecutive successes on attempt {attempt}/{maxAttempts}: {ex.Message}"
                    : $"[DIAG] attempt {attempt}/{maxAttempts} failed: {ex.Message}");

                consecutiveSuccesses = 0;

                if (attempt == maxAttempts)
                {
                    throw;
                }
            }

            await Task.Delay(delay);
        }
    }

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}

/// <summary>
/// Test double for <see cref="IEmailService"/>: always reports delivery success (no external
/// network involved) and records every send so integration tests can read back plaintext OTP
/// codes that only ever exist in the email body (the DB only stores a PBKDF2/SHA-256 hash).
/// </summary>
public sealed class CapturingEmailService : IEmailService
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public Task<bool> SendEmailAsync(string to, string subject, string htmlContent)
    {
        _sent.Enqueue(new SentEmail(to, subject, htmlContent, DateTime.UtcNow));
        return Task.FromResult(true);
    }

    /// <summary>
    /// Returns the most recently captured email sent to <paramref name="to"/>, or
    /// <c>null</c> when none has been sent yet.
    /// </summary>
    public SentEmail? LastSentTo(string to) =>
        _sent.Where(email => string.Equals(email.To, to, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(email => email.SentAtUtc)
            .FirstOrDefault();

    public sealed record SentEmail(string To, string Subject, string HtmlContent, DateTime SentAtUtc);
}
