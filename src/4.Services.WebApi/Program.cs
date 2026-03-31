using JOIN.Application;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Persistence.Configuration;
using JOIN.Persistence.Contexts;
using JOIN.Services.WebApi.Middlewares;
using JOIN.Services.WebApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore; // <-- New using for modern API UI
using JOIN.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 1. ADD CORE SERVICES (Dependency Injection)
// ============================================================================

builder.Services.AddControllers();

// .NET 10 Native OpenAPI generation (Replaces AddSwaggerGen)
builder.Services.AddOpenApi(); 
builder.Services.AddEndpointsApiExplorer();

// Register the Global Exception Handler (RFC 7807 Problem Details)
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

// ============================================================================
// 2. ADD CLEAN ARCHITECTURE LAYERS
// ============================================================================

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();


// Application Layer (MediatR, FluentValidation Pipeline)
builder.Services.AddApplicationServices();

// Infrastructure Layer (Dapper Connection Factory, Integrations)
builder.Services.AddInfrastructure(builder.Configuration);

// Infrastructure Layer (EF Core, Dapper Context, Repositories, UnitOfWork)
builder.Services.AddPersistenceServices(builder.Configuration);

// ============================================================================
// 3. ADD IDENTITY & SECURITY SERVICES
// ============================================================================

builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

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
        
        logger.LogInformation("Applying database migrations...");
        var context = services.GetRequiredService<ApplicationDbContext>();
        await context.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");

        var seeder = services.GetRequiredService<JOIN.Persistence.DatabaseSeeder>();
        await seeder.SeedAsync(); // Seed the database with initial data (roles, default users, etc.)

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // Generates the JSON document at /openapi/v1.json
    app.MapScalarApiReference(); // The modern UI at /scalar/v1
}

app.UseHttpsRedirection();

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllers();


if (app.Environment.IsDevelopment())
{
    app.MapGet("/", () => Results.Redirect("/scalar/v1"));
}

app.Run();