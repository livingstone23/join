using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http;
using Dapper;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using JOIN.Infrastructure.Audit;
using JOIN.Infrastructure.HealthChecks;
using JOIN.Infrastructure.Messaging.Logging;
using JOIN.Infrastructure.Messaging.SendGrid;
using JOIN.Infrastructure.Persistence;
using JOIN.Infrastructure.Security;
using JOIN.Infrastructure.Security.Jwt;
using JOIN.Infrastructure.Security.Mfa;
using JOIN.Infrastructure.Messaging.Sms;
using JOIN.Infrastructure.Storage;
using JOIN.Infrastructure.Storage.AzureBlob;
using JOIN.Infrastructure.Storage.Local;
using JOIN.Infrastructure.Storage.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;



namespace JOIN.Infrastructure;



/// <summary>
/// Extension methods for registering infrastructure services in the DI container.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers all infrastructure-layer services, adapters, and options.
    /// This is the canonical entry point called by the Presentation layer (WebApi).
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configuration">The application configuration used to bind options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ------------------------------------------------------------------
        // Data access: engine-agnostic Dapper connection factory (Singleton
        // because it holds no mutable state).
        // ------------------------------------------------------------------
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();

        // Npgsql reads `date` columns as DateOnly; the DTOs expose DateTime (process-wide, idempotent).
        SqlMapper.AddTypeHandler(new DateOnlyCompatibleDateTimeHandler());

        // ------------------------------------------------------------------
        // Security services
        // ------------------------------------------------------------------
        services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<ICalendarRecurrenceExpander, JOIN.Infrastructure.Calendars.IcalRecurrenceExpander>();
        services.AddScoped<ISecurityEventLogger, SecurityEventLogger>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IMfaTotpValidator, MfaTotpValidator>();
        services.AddScoped<ISmsService, NoOpSmsService>();

        // ------------------------------------------------------------------
        // Messaging: Email adapter (Adapter Pattern — Pillar 4).
        // Switch between providers via the "Email:Provider" configuration key:
        //   - "Log"      → LoggingEmailAdapter (dev only — no outbound traffic)
        //   - "SendGrid" → SendGridEmailAdapter + Polly v8 standard resilience pipeline
        // Unknown values and missing keys fall back to SendGrid so production never
        // silently routes through the dev log sink. The Development override file
        // (appsettings.Development.json) is the only place that sets "Log".
        // ------------------------------------------------------------------
        var emailProvider = configuration["Email:Provider"];

        if (string.Equals(emailProvider, "Log", StringComparison.OrdinalIgnoreCase))
        {
            // Transient lifetime matches the AddHttpClient<> registration used for
            // SendGridEmailAdapter in the else-branch. HealthCheckEmailPublisher is a
            // singleton and would reject a scoped dependency at DI validation time.
            services.AddTransient<IEmailService, LoggingEmailAdapter>();
        }
        else
        {
            services.Configure<SendGridOptions>(configuration.GetSection("SendGrid"));
            services.AddHttpClient<IEmailService, SendGridEmailAdapter>()
                .AddStandardResilienceHandler(options =>
                {
                    options.Retry.MaxRetryAttempts = 3;
                    options.Retry.ShouldHandle = args => ValueTask.FromResult(
                        args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                            or HttpStatusCode.TooManyRequests
                            or >= HttpStatusCode.InternalServerError
                        || args.Outcome.Exception is HttpRequestException);
                    // CircuitBreaker, AttemptTimeout, TotalRequestTimeout: preset defaults.
                });
        }

        // ------------------------------------------------------------------
        // File storage adapters (SPEC 49-post). Four providers behind a
        // single contract: local disk (always registered — the de-facto
        // fallback for legacy rows and dev/testing), Azure Blob, Amazon
        // S3 and Cloudflare R2. The active provider is selected by
        // FileStorage:Provider; the other sections can stay populated so
        // that pre-existing rows continue to download from the service
        // where they were uploaded.
        // ------------------------------------------------------------------
        // A relative RootPath is anchored to the host's ContentRootPath rather than
        // the process working directory, which differs between `dotnet run`, IIS
        // and containers and would otherwise scatter attachments across folders.
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection("FileStorage"))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (!Path.IsPathRooted(options.Local.RootPath))
                {
                    options.Local.RootPath = Path.Combine(environment.ContentRootPath, options.Local.RootPath);
                }
            })
            // Falla al arranque si FileStorage:Provider apunta a una
            // sección incompleta. Un error de despliegue se ve al
            // levantar la API, no en la primera subida de un usuario.
            .Validate(options => FileStorageOptionsValidator.IsValid(options, configuration),
                "FileStorage configuration is invalid. Check that the section for FileStorage:Provider has all required values.")
            .ValidateOnStart();

        // Local: siempre registrado. Es el provider por defecto, el de
        // pruebas, y el que permite seguir leyendo adjuntos locales
        // después de pasar a la nube.
        services.AddKeyedScoped<IFileStorageService, LocalDiskFileStorageAdapter>(StorageProviderKind.Local);

        if (configuration.GetSection("FileStorage:AzureBlob").Exists()
            && configuration.GetSection("FileStorage:AzureBlob").Get<AzureBlobStorageOptions>() is { } azureOptions
            && azureOptions.IsConfigured)
        {
            services.AddKeyedScoped<IFileStorageService, AzureBlobFileStorageAdapter>(StorageProviderKind.AzureBlob);
        }

        if (configuration.GetSection("FileStorage:S3").Exists()
            && configuration.GetSection("FileStorage:S3").Get<S3StorageOptions>() is { } s3Options
            && s3Options.IsConfigured)
        {
            services.AddKeyedScoped<IFileStorageService, S3CompatibleFileStorageAdapter>(StorageProviderKind.S3);
        }

        if (configuration.GetSection("FileStorage:CloudflareR2").Exists()
            && configuration.GetSection("FileStorage:CloudflareR2").Get<CloudflareR2StorageOptions>() is { } r2Options
            && r2Options.IsConfigured)
        {
            services.AddKeyedScoped<IFileStorageService, S3CompatibleFileStorageAdapter>(StorageProviderKind.CloudflareR2);
        }

        services.AddScoped<IFileStorageResolver, FileStorageResolver>();

        return services;
    }

    /// <summary>
    /// Backward-compatible wrapper. Delegates to <see cref="AddInfrastructureServices"/>.
    /// Kept so that existing call sites in Program.cs continue to compile without changes.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        => services.AddInfrastructureServices(configuration);

    /// <summary>
    /// Registers infrastructure health check alerting services.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configuration">The application configuration used to bind options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddInfrastructureHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HealthCheckAlertOptions>(configuration.GetSection("HealthCheckAlerts"));
        services.AddSingleton<IHealthCheckPublisher, HealthCheckEmailPublisher>();

        return services;
    }
}
