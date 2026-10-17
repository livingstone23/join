


using System.Reflection;
using FluentValidation;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Customers;
using JOIN.Application.UseCases.Admin.PersonAddresses;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Admin.Persons;
using JOIN.Application.UseCases.Security.SystemOptions;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Application.UseCases.Calendars.ChannelIntake;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Application.Mappings;
using JOIN.Application.Services.Customers;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Services.Finance;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Finance.CompanyCurrencies;
using JOIN.Application.UseCases.Customers.CustomerStages;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadStages;
using JOIN.Application.UseCases.Customers.SalesTeams;
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
        services.AddScoped<ITicketAttachmentSettingsMapper, TicketAttachmentSettingsMapper>();
        services.AddScoped<ISystemOptionMapper, SystemOptionMapper>();
        services.AddScoped<IRoleSystemOptionMapper, RoleSystemOptionMapper>();
        services.AddScoped<IRoleMapper, RoleMapper>();
        services.AddScoped<ICustomerCodeGenerator, CustomerCodeGenerator>();
        // SPEC 55 — customers: idempotent activation and one default stage per company.
        services.AddScoped<ICustomerActivationService, CustomerActivationService>();
        services.AddScoped<CustomerStageDefaultCoordinator>();
        // SPEC 56 — finance: base currency rules, rate resolution and conversion.
        services.AddScoped<CompanyBaseCurrencyCoordinator>();
        services.AddScoped<CompanyBaseCurrencyChangeGuard>();
        services.AddScoped<IExchangeRateResolver, ExchangeRateResolver>();
        services.AddSingleton<IMoneyConverter, MoneyConverter>();
        // SPEC 57 — product reference prices lock the base currency (SPEC 56 rule 6).
        services.AddScoped<IBaseCurrencyAmountSource, ProductReferencePriceAmountSource>();
        // SPEC 59 — commercial settings: company rules and the lead stage kinds.
        services.AddScoped<ILeadCompanySettingsProvider, LeadCompanySettingsProvider>();
        services.AddScoped<LeadStageKindCoordinator>();
        // SPEC 60 — sales teams: primary membership, one leader per team, lead scope and closing on company leave.
        services.AddScoped<SalesTeamMemberPrimaryCoordinator>();
        services.AddScoped<SalesTeamLeaderCoordinator>();
        services.AddScoped<JOIN.Application.UseCases.Customers.SalesTeams.Members.Commands.SalesTeamMemberContext>();
        services.AddScoped<ISalesTeamMembershipCloser, SalesTeamMembershipCloser>();
        services.AddScoped<ILeadAccessScopeResolver, LeadAccessScopeResolver>();
        // SPEC 61 — leads: access, code, stage moves, value in base currency, owners and lead amounts lock the base currency.
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadAccessGuard>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadCommandContext>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadCodeGenerator>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadStageAdvanceCoordinator>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadValueCalculator>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadOwnerRules>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadReferenceValidator>();
        services.AddScoped<JOIN.Application.UseCases.Customers.Leads.Common.LeadWorkRules>();
        services.AddScoped<IBaseCurrencyAmountSource, JOIN.Application.UseCases.Customers.Leads.Common.LeadsAmountSource>();
        // SPEC 64 — commercial reports: filters, period and report scope.
        services.AddScoped<JOIN.Application.UseCases.Customers.LeadReports.LeadReportContext>();
        services.AddScoped<IAuthenticatedSessionIssuer, AuthenticatedSessionIssuer>();
        services.AddScoped<IMfaLoginChallengeIssuer, MfaLoginChallengeIssuer>();
        services.AddScoped<PersonAddressDefaultCoordinator>();
        services.AddScoped<PersonContactPrimaryCoordinator>();
        services.AddScoped<PersonEmploymentCurrentCoordinator>();
        services.AddScoped<PersonBusinessProfileActiveCoordinator>();
        services.AddScoped<PersonFinancialProfileCurrentCoordinator>();
        services.AddScoped<TicketUserCompanySuperAdminCoordinator>();
        services.AddScoped<TicketUserCompanyCapabilityResolver>();
        services.AddScoped<TicketStatusTransitionGuard>();
        services.AddScoped<TicketInitialStatusResolver>();
        services.AddScoped<TicketDtoAssembler>();
        services.AddScoped<TicketCodeGenerator>();
        services.AddScoped<SoftDeleteRestorer>();
        services.AddScoped<RolePermissionCacheInvalidator>();
        services.AddScoped<CompanyPermissionCacheInvalidator>();
        services.AddScoped<PersonCascadeCoordinator>();
        services.AddScoped<SystemOptionCascadeCoordinator>();
        services.AddScoped<TicketCascadeCoordinator>();

        // SPEC 44 — calendar module.
        services.AddScoped<ICalendarMapper, CalendarMapper>();
        services.AddScoped<CalendarCompanyGuard>();
        services.AddScoped<CalendarCatalogReader>();
        services.AddScoped<CalendarUserProvisioner>();
        services.AddScoped<CalendarUserDefaultCoordinator>();
        services.AddScoped<CalendarEventInitialStatusResolver>();
        services.AddScoped<CalendarEventLogWriter>();
        services.AddScoped<CalendarEventScheduleGuard>();
        services.AddScoped<CalendarEventCommandProcessor>();
        services.AddScoped<CalendarQueryReader>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarPanel.CalendarPanelAccess>();
        services.AddScoped<CalendarEventQueryService>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.UserCalendars.UserCalendarCommandContext>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.CalendarNonWorkingPeriodWriter>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.CalendarUserConfigurationQuery>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarCompanies.CalendarCompanySettingsValidator>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarCompanies.CalendarCompanyProvisioning>();
        services.AddScoped<JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.CalendarStatusCompanyRemovalGuard>();

        // SPEC 45 — channel agent intake and pending persons.
        services.AddScoped<IntakeChannelReader>();
        services.AddScoped<IntakeRequesterResolver>();
        services.AddScoped<IntakeEventGuard>();
        services.AddScoped<PersonIntakeRegistrar>();
        services.AddScoped<PersonPendingConfirmationReader>();
        services.AddScoped<PersonPendingReviewCloser>();

        return services;
    
    }

}
