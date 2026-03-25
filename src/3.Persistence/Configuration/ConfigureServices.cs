// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Admin;
using JOIN.Persistence.Contexts;
using JOIN.Persistence.Repositories;
using JOIN.Persistence.Repositories.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;



namespace JOIN.Persistence.Configuration;



/// <summary>
/// Extension methods for setting up persistence services in an <see cref="IServiceCollection" />.
/// Centralizes dependency injection for the Persistence layer to maintain Clean Architecture boundaries.
/// </summary>
public static class ConfigureServices
{
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
        
        // El UnitOfWork ahora es híbrido y manejará el resto de los módulos dinámicamente.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // 6. SERVICIOS DE APOYO
        services.AddScoped<JOIN.Persistence.DatabaseSeeder>();

        return services;
    }
}