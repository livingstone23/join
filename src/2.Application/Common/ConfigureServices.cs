


using System.Reflection;
using FluentValidation;
using JOIN.Application.Interface.Admin;
using JOIN.Application.UseCases.Admin.PersonAddresses;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Application.Mappings;
using JOIN.Application.Services.Admin;
using JOIN.Application.Mappings.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using JOIN.Application.Mappings.Security.SystemOption;
using MediatR;
using Microsoft.Extensions.DependencyInjection;



namespace JOIN.Application.Common;



/// <summary>
/// Extension methods for setting up application layer services in an <see cref="IServiceCollection" />.
/// </summary>
public static class ConfigureServices
{
    /// <summary>
    /// Adds MediatR, FluentValidation, and Pipeline Behaviors to the service collection.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Register MediatR and automatically discover all Handlers
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(assembly);

            // Register UnhandledExceptionBehavior FIRST (outermost) so it can capture any
            // exception thrown anywhere in the pipeline below — including from ValidationBehavior,
            // PerformanceBehavior, LoggingBehavior, TransactionBehavior, or the handler itself.
            // Business-expected exceptions (ValidationException, NotFoundException, DomainException)
            // are rethrown untouched and intentionally NOT logged here (GlobalExceptionHandler already
            // maps them to standard HTTP responses).
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehavior<,>));

            // Register PerformanceBehavior as the SECOND behavior in the pipeline so it measures
            // the total elapsed time as experienced by the HTTP client (including ValidationBehavior,
            // LoggingBehavior, and TransactionBehavior overhead such as BeginTransactionAsync/CommitAsync).
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

            // Register the validation pipeline behavior
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            // Register LoggingBehavior AFTER validation so "Started" only fires for requests that
            // actually pass validation, and BEFORE the transaction so "Finished" includes the
            // transaction commit latency. No try/catch — the "Finished" log is simply skipped
            // when the downstream throws.
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));

            // Register the transaction pipeline behavior immediately AFTER LoggingBehavior.
            // It uses a runtime interface check, so requests that do NOT implement
            // ITransactionalCommand<TResponse> bypass it with zero overhead.
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        });

        // Register FluentValidation and automatically discover all Validators
        services.AddValidatorsFromAssembly(assembly);

        // Register Mapperly mappers
        services.AddScoped<IPersonMapper, PersonMapper>();
        services.AddScoped<ITicketMapper, TicketMapper>();
        services.AddScoped<ITicketCompanyDefaultMapper, TicketCompanyDefaultMapper>();
        services.AddScoped<ISystemOptionMapper, SystemOptionMapper>();
        services.AddScoped<IRoleSystemOptionMapper, RoleSystemOptionMapper>();
        services.AddScoped<ICustomerCodeGenerator, CustomerCodeGenerator>();
        services.AddScoped<PersonAddressDefaultCoordinator>();
        services.AddScoped<PersonContactPrimaryCoordinator>();
        services.AddScoped<PersonEmploymentCurrentCoordinator>();
        services.AddScoped<PersonBusinessProfileActiveCoordinator>();
        services.AddScoped<PersonFinancialProfileCurrentCoordinator>();

        return services;
    
    }

}
