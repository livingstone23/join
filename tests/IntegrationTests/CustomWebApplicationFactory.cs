using JOIN.Application.Interface;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
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
    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private string _connectionString = string.Empty;

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
            // Register as Transient to match the production lifetime
            // (services.AddTransient<IEmailService, SendGridEmailAdapter>() in
            // JOIN.Infrastructure.DependencyInjection). Singleton consumers like
            // HealthCheckEmailPublisher would otherwise fail DI scope validation.
            services.AddTransient(_ => Mock.Of<IEmailService>(
                e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == Task.FromResult(true)));
        });
    }

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        // Testcontainers' built-in wait strategy runs "sqlcmd -Q SELECT 1" INSIDE the
        // container network namespace, which only proves SQL Server is ready internally.
        // Observed on GitHub Actions ubuntu-latest runners: connection attempts fail
        // inconsistently — some fail fast (~3s), others hang the full connect timeout
        // (~15s) — the classic signature of Microsoft.Data.SqlClient's ManagedSni
        // resolving "localhost" to ::1 (IPv6) first on some attempts, where Docker's
        // published port isn't bound, instead of 127.0.0.1 (IPv4) where it is. Force
        // IPv4First so every attempt resolves consistently, and bump ConnectTimeout as
        // a secondary safety margin.
        var builder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            ConnectTimeout = 30,
            IPAddressPreference = SqlConnectionIPAddressPreference.IPv4First
        };
        _connectionString = builder.ConnectionString;

        await WaitUntilReachableAsync();
    }

    private async Task WaitUntilReachableAsync()
    {
        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (SqlException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
