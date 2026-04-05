using JOIN.Application.Interface;
using JOIN.Infrastructure.Persistence;
using JOIN.Infrastructure.Security;
using JOIN.Infrastructure.Security.Jwt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;



namespace JOIN.Infrastructure;



/// <summary>
/// Extension methods for registering infrastructure services in the DI container.
/// </summary>
public static class DependencyInjection
{


    /// <summary>
    /// Adds infrastructure dependencies to the service collection.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ... (other configurations for EF Core, Identity, etc.)

        // Register the Dapper connection factory as a Singleton
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Register JWT generation services.
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPermissionService, PermissionService>();

        return services;
    }
}
