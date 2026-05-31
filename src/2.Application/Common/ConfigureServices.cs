


using System.Reflection;
using FluentValidation;
using JOIN.Application.Interface.Admin;
using JOIN.Application.UseCases.Admin.PersonAddresses;
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
            
            // Register the validation pipeline behavior
            config.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
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

        return services;
    
    }

}
