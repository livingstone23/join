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
        // container network namespace, which only proves SQL Server accepts a login —
        // not that it has finished its own startup (tempdb/model/msdb creation). On
        // GitHub Actions runners the app can keep failing to query for 40-60+ seconds
        // after the container reports "ready", with a login-succeeds-but-query-fails
        // pattern. ConnectTimeout/IPv4First were tried and ruled out — the failure
        // pattern was byte-for-byte identical with and without them. So this probe
        // runs a real query (not just Open()), with a long budget, before handing the
        // connection string to the app — Program.cs has its own matching retry loop
        // around the startup migration check for the same reason.
        var builder = new SqlConnectionStringBuilder(_dbContainer.GetConnectionString())
        {
            ConnectTimeout = 30,
            IPAddressPreference = SqlConnectionIPAddressPreference.IPv4First
        };
        _connectionString = builder.ConnectionString;

        await WaitUntilQueryableAsync();
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
                return;
            }
            catch (SqlException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay);
            }
        }
    }

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
