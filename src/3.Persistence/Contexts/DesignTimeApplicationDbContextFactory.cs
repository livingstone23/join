using JOIN.Application.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace JOIN.Persistence.Contexts;

/// <summary>
/// Creates <see cref="ApplicationDbContext"/> instances for EF Core design-time operations.
/// </summary>
public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    /// <inheritdoc />
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var basePath = ResolveConfigurationBasePath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("The 'DefaultConnection' connection string is not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(
            connectionString,
            builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));

        var currentUserService = new DesignTimeCurrentUserService();
        var interceptor = new AuditableEntitySaveChangesInterceptor(currentUserService);

        return new ApplicationDbContext(optionsBuilder.Options, interceptor, currentUserService);
    }

    private static string ResolveConfigurationBasePath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        var candidatePaths = new[]
        {
            Path.Combine(currentDirectory, "src", "4.Services.WebApi"),
            Path.Combine(currentDirectory, "..", "4.Services.WebApi"),
            Path.Combine(currentDirectory, "..", "..", "4.Services.WebApi")
        };

        var resolvedPath = candidatePaths.FirstOrDefault(Directory.Exists);
        if (resolvedPath is null)
        {
            throw new DirectoryNotFoundException("Unable to locate the WebApi configuration directory for design-time EF operations.");
        }

        return resolvedPath;
    }

    private sealed class DesignTimeCurrentUserService : ICurrentUserService
    {
        public string? UserId => "DesignTime";

        public Guid CompanyId => Guid.Empty;

        public bool IsAuthenticated => false;
    }
}