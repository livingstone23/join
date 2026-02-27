using JOIN.Application.Interface;
using JOIN.Infrastructure.Contexts;
using JOIN.Services.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 1. ADD CORE SERVICES (Dependency Injection)
// ============================================================================

// Enables the use of standard API Controllers instead of Minimal APIs
builder.Services.AddControllers();

// Enables OpenAPI/Swagger documentation
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(); // Standard Swagger UI support

// ============================================================================
// 2. ADD INFRASTRUCTURE & PERSISTENCE SERVICES
// ============================================================================

// Register the HttpContextAccessor (Required to read the JWT token claims)
builder.Services.AddHttpContextAccessor();

// Register Application Services
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Register the EF Core Interceptor for Auditing and Soft Delete
builder.Services.AddScoped<AuditableEntitySaveChangesInterceptor>();

// Register the Database Context
// NOTE: Replace "DefaultConnection" with your actual connection string name in appsettings.json
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
    
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .AddInterceptors(interceptor);
});

// ============================================================================
// 3. ADD IDENTITY & SECURITY SERVICES
// ============================================================================

// Configure ASP.NET Core Identity for Multi-tenant RBAC
builder.Services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();
// TODO later: .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>()

var app = builder.Build();

// ============================================================================
// 4. CONFIGURE THE HTTP REQUEST PIPELINE (Middleware)
// ============================================================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// SECURITY MIDDLEWARES: Order is critical here!
app.UseAuthentication(); // 1. Who are you? (Validates JWT/Cookies)
app.UseAuthorization();  // 2. Are you allowed to do this? (Validates Roles/Policies)

// Map standard Controller endpoints
app.MapControllers();

// Start the application
app.Run();