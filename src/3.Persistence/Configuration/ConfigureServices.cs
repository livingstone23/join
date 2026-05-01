// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using JOIN.Persistence.Repositories.Admin;
using JOIN.Persistence.Repositories.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;



namespace JOIN.Persistence.Configuration;



/// <summary>
/// Extension methods for setting up persistence services in an <see cref="IServiceCollection" />.
/// Centralizes dependency injection for the Persistence layer to maintain Clean Architecture boundaries.
/// </summary>
public static class ConfigureServices
{
    /// <summary>
    /// Registers persistence services for commands (EF Core), repositories, and support services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for fluent chaining.</returns>
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. REPOSITORIO GENÉRICO (PIEZA CLAVE PARA ESCALABILIDAD)
        // Registramos el tipo abierto para que cualquier IGenericRepository<T> 
        // se resuelva automáticamente a GenericRepository<T>.
        services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

        // 2. CONTEXTO DAPPER
        // Mantenemos el contexto de Dapper para consultas de alto rendimiento (Queries).
        services.AddSingleton<DapperContext>();

        // 3. INTERCEPTOR DE AUDITORÍA
        // Debe registrarse antes que el DbContext para ser inyectado correctamente.
        services.AddScoped<AuditableEntitySaveChangesInterceptor>();

        // 4. CONFIGURACIÓN DE BASE DE DATOS (EF CORE)
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
            options.AddInterceptors(interceptor);

            // Configuración predeterminada para SQL Server
            options.UseSqlServer(connectionString, 
                builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName));
        });

        // 5. REPOSITORIOS ESPECÍFICOS Y UNIT OF WORK
        // Registramos el repositorio de clientes explícitamente para permitir lógica personalizada.
        services.AddScoped<ICustomersRepository, CustomersRepository>();
        services.AddScoped<IRoleSystemOptionsRepository, RoleSystemOptionsRepository>();
        
        // El UnitOfWork ahora es híbrido y manejará el resto de los módulos dinámicamente.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 6. SERVICIOS DE APOYO
        services.AddScoped<JOIN.Persistence.DatabaseSeeder>();
        services.AddScoped<ICompanyCatalogSeeder>(sp => sp.GetRequiredService<JOIN.Persistence.DatabaseSeeder>());

        return services;
    }

    /// <summary>
    /// Registers database health checks based on the configured provider.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the database provider or default connection string is not configured.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when the configured database provider is not supported.
    /// </exception>
    public static IServiceCollection AddPersistenceHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["DatabaseProvider"];
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new InvalidOperationException("DatabaseProvider is required to configure persistence health checks.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required to configure persistence health checks.");
        }

        var builder = services.AddHealthChecks();

        switch (provider.Trim())
        {
            case "SqlServer":
                builder.AddSqlServer(
                    connectionString: connectionString,
                    name: "sqlserver",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["db", "sql"]);
                break;

            case "PostgreSQL":
            case "Postgres":
                builder.AddNpgSql(
                    connectionString: connectionString,
                    name: "postgresql",
                    failureStatus: HealthStatus.Unhealthy,
                    tags: ["db", "pg"]);
                break;

            default:
                throw new NotSupportedException($"DatabaseProvider '{provider}' is not supported for persistence health checks.");
        }

        return services;
    }
}