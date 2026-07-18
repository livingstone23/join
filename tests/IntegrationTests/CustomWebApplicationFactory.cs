using JOIN.Application.Interface;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _dbContainer.GetConnectionString()
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

    public Task InitializeAsync() => _dbContainer.StartAsync();

    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
