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
/// </summary>
public static class ConfigureServices
{

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Register Dapper Context (Singleton because it only holds configuration logic)
        services.AddSingleton<DapperContext>();

        // 2. Register the Interceptor FIRST (CRITICAL FIX)
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();

        // 3. Register EF Core Context with its Auditing Interceptor
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            // Now the service provider (sp) will successfully find the interceptor
            var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
            
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                   .AddInterceptors(interceptor);
        });

        // 4. Register Repositories and UnitOfWork
        services.AddScoped<ICustomersRepository, CustomersRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();


        // Add database seeding service
        services.AddScoped<JOIN.Infrastructure.Persistence.DatabaseSeeder>();

        return services;
   
    }

}