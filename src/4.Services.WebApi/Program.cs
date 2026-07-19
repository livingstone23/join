using System.Text;
using JOIN.Application;
using JOIN.Application.Common;
using JOIN.Application.Common.Security;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Infrastructure;
using JOIN.Infrastructure.Security.Validators;
using JOIN.Persistence.Configuration;
using JOIN.Persistence.Contexts;
using JOIN.Services.WebApi.Filters;
using JOIN.Services.WebApi.Middlewares;
using JOIN.Services.WebApi.Services.RateLimiting;
using JOIN.Services.WebApi.Services;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore; // <-- New using for modern API UI
using Serilog;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;

// ============================================================================
// SERILOG BOOTSTRAP LOGGER
// Captures host startup errors before the real logger (built from configuration)
// exists. Replaced by UseSerilog(...) below once the WebApplicationBuilder is up.
// ============================================================================
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{

var builder = WebApplication.CreateBuilder(args);

// Replace the default Microsoft.Extensions.Logging factory with Serilog,
// configured entirely from the "Serilog" section of appsettings.{env}.json.
builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// ============================================================================
// 1. ADD CORE SERVICES (Dependency Injection)
// ============================================================================

builder.Services.AddScoped<DynamicAuthorizationFilter>();
builder.Services.AddControllers(options => options.Filters.AddService<DynamicAuthorizationFilter>());

// .NET 10 Native OpenAPI generation (Replaces AddSwaggerGen)
// API versioning + explorer wired first so IApiVersionDescriptionProvider is available
// before OpenAPI document registration iterates over discovered versions.
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddEndpointsApiExplorer();

// Register one OpenAPI document per supported API version, named to match
// GroupNameFormat = "'v'VVV" (e.g. ApiVersion(1,0) -> "v1").
// Today only v1.0 is supported (per SPEC 08 scope). When new ApiVersion
// attributes are added to controllers, add a matching AddOpenApi("vN") here.
// Enumerating versions via IApiVersionDescriptionProvider at this point
// requires BuildServiceProvider() which conflicts with Serilog's bootstrap
// logger freeze in this repo (InvalidOperationException: The logger is
// already frozen). Document name list stays explicit for that reason.
builder.Services.AddOpenApi("v1");

// Register the global exception handler and native RFC 7807 ProblemDetails services.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;

        if (!context.ProblemDetails.Extensions.ContainsKey("traceId"))
        {
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        }

        if (!context.ProblemDetails.Extensions.ContainsKey("timestamp"))
        {
            context.ProblemDetails.Extensions["timestamp"] = DateTimeOffset.UtcNow;
        }
    };
});

// ============================================================================
// 2. ADD CLEAN ARCHITECTURE LAYERS
// ============================================================================

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var passwordPolicySection = builder.Configuration.GetSection("PasswordPolicy");
builder.Services.Configure<PasswordPolicySettings>(passwordPolicySection);
var passwordPolicySettings = passwordPolicySection.Get<PasswordPolicySettings>() ?? new PasswordPolicySettings();

var areaPaginationSection = builder.Configuration.GetSection("AreaPagination");
builder.Services.Configure<PaginationSettings>(areaPaginationSection);

builder.Services.Configure<PerformanceSettings>(builder.Configuration.GetSection("Performance"));

// Application Layer (MediatR, FluentValidation Pipeline)
builder.Services.AddApplicationServices();

// Infrastructure Layer (Dapper Connection Factory, Integrations)
builder.Services.AddInfrastructure(builder.Configuration);

// Infrastructure Layer (EF Core, Dapper Context, Repositories, UnitOfWork)
builder.Services.AddPersistenceServices(builder.Configuration);
builder.Services.AddPersistenceHealthChecks(builder.Configuration);
builder.Services.AddInfrastructureHealthChecks(builder.Configuration);
builder.Services.AddJoinRateLimiting(builder.Configuration);
builder.Services.AddHealthChecksUI(options =>
{
    options.SetEvaluationTimeInSeconds(30);
    options.AddHealthCheckEndpoint("JOIN CRM", "/health/ready");
})
.AddInMemoryStorage();

// ============================================================================
// 3. ADD IDENTITY & SECURITY SERVICES
// ============================================================================

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = Math.Max(8, passwordPolicySettings.MinimumLength);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddPasswordValidator<CustomPasswordValidator>()
.AddDefaultTokenProviders();

var jwtKey = builder.Configuration["Jwt:Key"] ?? "JOIN_Development_Key_Change_This_In_Production_2026!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "JOIN.Services.WebApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "JOIN.Client";

builder.Services.AddMemoryCache();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier,
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// ============================================================================
// 4. AUTOMATIC DATABASE MIGRATION
// ============================================================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();

        await context.Database.MigrateAsync();

        if (pendingMigrations.Count > 0)
        {
            logger.LogInformation(
                "Applied {PendingCount} pending database migration(s): {MigrationNames}",
                pendingMigrations.Count,
                string.Join(", ", pendingMigrations));
        }

        var seeder = services.GetRequiredService<JOIN.Persistence.DatabaseSeeder>();

        if (pendingMigrations.Count > 0)
        {
            await seeder.SeedAsync();
        }
        else if (app.Environment.IsDevelopment())
        {
            logger.LogInformation("Running idempotent menu and permissions seed (Development).");
            await seeder.SeedMenuAndPermissionsAsync();
        }

    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating the database.");
        throw; 
    }
}

// ============================================================================
// 5. CONFIGURE THE HTTP REQUEST PIPELINE (Middleware)
// ============================================================================

app.UseExceptionHandler(); 
app.UseForwardedHeaders();
app.UseMiddleware<DynamicStrictRateLimitingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // MapOpenApi default route is /openapi/{documentName}.json — auto-handles
    // every document registered above. WithDocumentPerVersion() extension is
    // not shipped in Asp.Versioning 10.0.0 packages, so this is the canonical
    // equivalent for the OpenAPI surface.
    app.MapOpenApi();

    app.MapScalarApiReference(options =>
    {
        // DescribeApiVersions() reads IApiVersionDescriptionProvider from the
        // built app's service provider (post-build, no early BuildServiceProvider
        // needed). Drives Scalar's version selector. New ApiVersion attributes
        // require updating the AddOpenApi("vN") list above to keep both
        // /openapi/vN.json and the Scalar selector in sync.
        var descriptions = app.DescribeApiVersions();
        for (var i = 0; i < descriptions.Count; i++)
        {
            var description = descriptions[i];
            var isDefault = i == descriptions.Count - 1;
            options.AddDocument(description.GroupName, description.GroupName, isDefault: isDefault);
        }
    });
}

app.UseHttpsRedirection();
app.UseRateLimiter();

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllers();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
app.MapHealthChecksUI(options => options.UIPath = "/health-ui");


if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1"))
        .ExcludeFromDescription();
}

app.Run();

}
catch (Exception ex)
{
    Log.Fatal(ex, "JOIN host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make the implicit Program class visible to integration tests (WebApplicationFactory<Program>).
// Required because top-level statements generate an internal sealed Program class by default.
// Partial so it merges cleanly with the compiler-generated one — no runtime impact in production.
public partial class Program { }