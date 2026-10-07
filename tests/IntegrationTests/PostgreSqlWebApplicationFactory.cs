using JOIN.Application.Interface;
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
/// PostgreSQL variant of <see cref="CustomWebApplicationFactory"/> for the main_postgresql
/// fork (SPEC 39). Spins up an ephemeral PostgreSQL via Testcontainers, points the host at
/// it via <c>ConnectionStrings:DefaultConnection</c>, and applies the same test doubles
/// (<see cref="CapturingEmailService"/>, <see cref="TestCurrentUserService"/>). The host's own
/// startup migrates (<c>InitialPostgres</c>) and seeds — no extra migration logic lives here.
/// </summary>
public sealed class PostgreSqlWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Same eager-read constraint as CustomWebApplicationFactory: rate limits must be set via
    // environment variables before any host in this process builds.
    static PostgreSqlWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("RateLimiting__Global__PermitLimit", "1000000");
        Environment.SetEnvironmentVariable("RateLimiting__Strict__PermitLimit", "1000000");
    }

    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    /// <summary>
    /// Connection string of the ephemeral database, for tests that verify raw SQL behavior
    /// (filtered indexes) outside the EF Core model.
    /// </summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>
    /// Serializes host build on the lock shared with <see cref="CustomWebApplicationFactory"/>
    /// — see its <c>CreateHost</c> remarks on Serilog's static <see cref="Log.Logger"/>.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (CustomWebApplicationFactory._hostBuildLock)
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
                ["DatabaseProvider"] = "PostgreSQL",
                ["ConnectionStrings:DefaultConnection"] = ConnectionString
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<CapturingEmailService>();
            services.AddTransient<IEmailService>(sp => sp.GetRequiredService<CapturingEmailService>());

            services.RemoveAll<ICurrentUserService>();
            services.AddScoped<TestCurrentUserService>();
            services.AddScoped<ICurrentUserService>(sp => sp.GetRequiredService<TestCurrentUserService>());
        });
    }

    public async Task InitializeAsync()
    {
        // The postgres image's readiness wait (pg_isready inside the container) is reliable,
        // unlike the mssql image's first-boot restart — no extra stability probe needed.
        await _dbContainer.StartAsync();
        ConnectionString = _dbContainer.GetConnectionString();
    }

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
