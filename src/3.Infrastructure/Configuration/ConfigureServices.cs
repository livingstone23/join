using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Infrastructure.Contexts;
using JOIN.Infrastructure.Repositories;
using JOIN.Infrastructure.Repositories.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Infrastructure.Configuration;



/// <summary>
/// Extension methods for setting up infrastructure services in an <see cref="IServiceCollection" />.
/// Centralizes dependency injection for the Infrastructure layer to maintain Clean Architecture boundaries.
/// Includes support for Database Agnosticism (SQL Server / PostgreSQL).
/// </summary>
public static class ConfigureServices
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Register Dapper Context (Singleton because it only holds configuration logic)
        services.AddSingleton<DapperContext>();

        // 2. Register the Interceptor FIRST (CRITICAL FIX)
        // Ensures the interceptor is available in the DI container before configuring the DbContext.
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();

        // 3. Read Database Provider Configuration
        // Determines which database engine to use based on appsettings.json
        var databaseProvider = configuration["DatabaseProvider"] ?? "SqlServer";
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        // 4. Register EF Core Context with its Auditing Interceptor
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            // The service provider (sp) will successfully find the interceptor
            var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
            options.AddInterceptors(interceptor);

            // Dynamically select the Database Provider
            if (databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                // Requires NuGet: Npgsql.EntityFrameworkCore.PostgreSQL
                options.UseNpgsql(connectionString, 
                    builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
            else
            {
                // Default fallback: SQL Server
                // Requires NuGet: Microsoft.EntityFrameworkCore.SqlServer
                options.UseSqlServer(connectionString, 
                    builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
            }
        });

        // 5. Register Repositories and UnitOfWork
        // Make sure you have these interfaces and implementations created
        services.AddScoped<ICustomersRepository, CustomersRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 6. Add database seeding service
        services.AddScoped<JOIN.Infrastructure.Persistence.DatabaseSeeder>();

        return services;
    }
}