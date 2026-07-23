using System.Net.Sockets;
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

        // DIAGNOSTIC (temporary): four separate fixes (ConnectTimeout, IPv4First, a
        // startup-check retry budget up to ~100s, then ~3min) all failed to change the
        // outcome — every single connection attempt times out for the container's
        // entire lifetime, even though Testcontainers' internal "docker exec sqlcmd"
        // check succeeds. That rules out DNS/address-family and "still starting up";
        // it points at the published port not being reachable at all from wherever
        // `dotnet test` runs on this specific CI runner. Log exactly what Testcontainers
        // resolved, and do a raw TCP-only probe (no SQL protocol involved) so the next
        // CI log tells us definitively whether this is a Docker/network problem outside
        // of anything SqlClient-related, instead of guessing at more connection-string
        // tweaks.
        var mappedPort = _dbContainer.GetMappedPublicPort(1433);
        Console.WriteLine($"[DIAG] Testcontainers hostname={_dbContainer.Hostname} mappedPort={mappedPort}");

        var rawTcpReachable = await TryRawTcpConnectAsync(_dbContainer.Hostname, mappedPort);
        Console.WriteLine($"[DIAG] raw TCP connect to {_dbContainer.Hostname}:{mappedPort} succeeded={rawTcpReachable}");

        var builder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            ConnectTimeout = 30,
            IPAddressPreference = SqlConnectionIPAddressPreference.IPv4First
        };
        _connectionString = builder.ConnectionString;

        await WaitUntilQueryableAsync();
    }

    private static async Task<bool> TryRawTcpConnectAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromSeconds(10));
            return client.Connected;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DIAG] raw TCP connect failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private async Task WaitUntilQueryableAsync()
    {
        const int maxAttempts = 20;
        var delay = TimeSpan.FromSeconds(5);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync();
                Console.WriteLine($"[DIAG] WaitUntilQueryableAsync succeeded on attempt {attempt}/{maxAttempts}");
                return;
            }
            catch (SqlException ex) when (attempt < maxAttempts)
            {
                Console.WriteLine($"[DIAG] WaitUntilQueryableAsync attempt {attempt}/{maxAttempts} failed: {ex.Message}");
                await Task.Delay(delay);
            }
        }
    }

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
