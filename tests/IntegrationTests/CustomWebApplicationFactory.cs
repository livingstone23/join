using System.Collections.Concurrent;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Serilog;
using Testcontainers.PostgreSql;

namespace JOIN.IntegrationTests;

/// <summary>
/// WebApplicationFactory base for all integration tests in the <c>main_postgresql</c> fork
/// (SPEC 50, F5). Spins up an ephemeral PostgreSQL via Testcontainers before the host boots,
/// points the host at it via <c>ConnectionStrings:DefaultConnection</c>, sets
/// <c>DatabaseProvider=PostgreSQL</c>, and replaces the real <see cref="IEmailService"/> with a
/// mock so no test depends on an external network or sends real emails. EF Core migrations run
/// automatically at startup — see <c>Program.cs</c>, no extra migration logic lives here on
/// purpose. The <c>postgres:17</c> image's readiness probe is reliable; no extra stability wait
/// is needed (Testcontainers' own check is enough, unlike the mssql image).
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    /// <summary>
    /// Connection string of the ephemeral database, for tests that need to verify raw SQL
    /// behavior (filtered indexes, savepoints) outside the EF Core model.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    // Process-wide lock around host build. Serilog's <c>Log.Logger</c> is a static singleton;
    // <see cref="WebApplicationFactory{TEntryPoint}"/> instances created concurrently by xUnit
    // (one per <c>[Collection]</c> test class via <c>IClassFixture</c>) each invoke
    // <c>Program.Main</c>, which assigns a fresh bootstrap logger to <c>Log.Logger</c> and then
    // calls <c>UseSerilog(...)</c>. Without serialization, factory B overwrites the bootstrap
    // assignment before factory A's <c>ILoggerFactory</c> resolution freezes it — A's freeze
    // then collides with B's later freeze of the same instance:
    // <c>InvalidOperationException: The logger is already frozen.</c>
    protected static readonly object _hostBuildLock = new();

    /// <summary>
    /// Extra configuration values layered over the defaults, for derived factories that need a
    /// different host setup (e.g. module-enforcement variants).
    /// </summary>
    protected virtual IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
        new Dictionary<string, string?>();

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
            var settings = new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "PostgreSQL",
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            };

            foreach (var (key, value) in AdditionalConfiguration)
            {
                settings[key] = value;
            }

            config.AddInMemoryCollection(settings);
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

            // SPEC 38: replace the production CurrentUserService (HttpContext-only) with a
            // test double that keeps production semantics when an HttpContext is present but
            // falls back to a per-scope override (CompanyIdOverride) for EF-only scopes
            // created by GlobalQueryFiltersIntegrationTests.CreateScopeAsCompany. Without
            // this, the global tenant filters evaluate CompanyId == Guid.Empty and hide
            // every row from those scopes.
            services.RemoveAll<ICurrentUserService>();
            services.AddScoped<TestCurrentUserService>();
            services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<TestCurrentUserService>());
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();
        ConnectionString = _dbContainer.GetConnectionString();
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
    private readonly ConcurrentQueue<SentMessage> _messages = new();

    public Task<bool> SendEmailAsync(string to, string subject, string htmlContent)
    {
        _sent.Enqueue(new SentEmail(to, subject, htmlContent, DateTime.UtcNow));
        return Task.FromResult(true);
    }

    /// <summary>SPEC 72 — recipients on this domain are rejected, to exercise the <c>Failed</c> path without shared state.</summary>
    public const string RejectedDomain = "@rejected.test";

    /// <summary>
    /// SPEC 72 — captures the full message (copies, Reply-To, sender and attachments) so the
    /// integration tests can assert what the provider would have received. A recipient on
    /// <see cref="RejectedDomain"/> is answered as a provider rejection.
    /// </summary>
    public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Enqueue(new SentMessage(
            message.To,
            message.Cc.ToList(),
            message.FromEmail,
            message.FromName,
            message.ReplyTo,
            message.Subject,
            message.HtmlBody,
            message.Attachments.ToList(),
            DateTime.UtcNow));

        return Task.FromResult(message.To.EndsWith(RejectedDomain, StringComparison.OrdinalIgnoreCase)
            ? EmailSendResult.Fail("550 The recipient was rejected.")
            : EmailSendResult.Ok("captured-message-id"));
    }

    /// <summary>
    /// Returns the most recently captured email sent to <paramref name="to"/>, or
    /// <c>null</c> when none has been sent yet.
    /// </summary>
    public SentEmail? LastSentTo(string to) =>
        _sent.Where(email => string.Equals(email.To, to, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(email => email.SentAtUtc)
            .FirstOrDefault();

    /// <summary>
    /// SPEC 72 — the last full message captured, or <c>null</c> when none has been sent yet.
    /// </summary>
    public SentMessage? LastMessage() =>
        _messages.OrderByDescending(message => message.SentAtUtc).FirstOrDefault();

    /// <summary>SPEC 72 — the last full message captured for <paramref name="to"/>, or <c>null</c>.</summary>
    public SentMessage? LastMessageTo(string to) =>
        _messages.Where(message => string.Equals(message.To, to, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(message => message.SentAtUtc)
            .FirstOrDefault();

    public sealed record SentEmail(string To, string Subject, string HtmlContent, DateTime SentAtUtc);

    public sealed record SentMessage(
        string To,
        IReadOnlyList<string> Cc,
        string? FromEmail,
        string? FromName,
        string? ReplyTo,
        string Subject,
        string HtmlBody,
        IReadOnlyList<EmailAttachment> Attachments,
        DateTime SentAtUtc);
}
