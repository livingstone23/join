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
