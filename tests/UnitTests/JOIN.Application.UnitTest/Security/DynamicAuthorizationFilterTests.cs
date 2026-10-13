using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using JOIN.Application.Common.Settings;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="DynamicAuthorizationFilter"/> covering bypass mechanisms
/// (AllowAnonymous, SkipDynamicAuthorization, SuperAdmin), status codes (401 vs 403),
/// resource name inference, and the explicit <see cref="RequirePermissionAttribute"/> override.
/// </summary>
public sealed class DynamicAuthorizationFilterTests
{
    private const string SuperAdminRoleName = "SuperAdmin";

    [ApiController]
    [Route("api/v1/[controller]")]
    private sealed class PersonsController
    {
        [HttpGet]
        public IActionResult List() => null!;

        [HttpGet("export")]
        [RequirePermission(PermissionFlags.CanExport)]
        public IActionResult Export() => null!;

        [HttpPost("run")]
        [RequirePermission(PermissionFlags.CanExecute)]
        public IActionResult Run() => null!;

        [HttpGet]
        [PermissionResource("ExplicitResource")]
        public IActionResult WithExplicitResource() => null!;
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [PermissionResource("ClassResource")]
    private sealed class ClassResourceController
    {
        [HttpGet]
        public IActionResult Get() => null!;
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [AllowAnonymous]
    private sealed class AnonymousController
    {
        [HttpGet]
        public IActionResult Get() => null!;
    }

    [ApiController]
    [Route("api/v1/[controller]")]
    [SkipDynamicAuthorization]
    private sealed class SkipController
    {
        [HttpGet]
        public IActionResult Get() => null!;
    }

    /// <summary>
    /// Builds an <see cref="AuthorizationFilterContext"/> for a controller action with optional
    /// claims and HTTP method override.
    /// </summary>
    private static AuthorizationFilterContext BuildContext(
        Type controllerType,
        string methodName,
        string httpMethod = "GET",
        params (string type, string value)[] claims)
    {
        var methodInfo = controllerType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
        var controllerTypeInfo = controllerType.GetTypeInfo();

        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = methodInfo,
            ControllerTypeInfo = controllerTypeInfo,
            ControllerName = controllerType.Name,
            RouteValues = new Dictionary<string, string?>()
        };

        // Copy EndpointMetadata from the controller + action (mimics ASP.NET Core's metadata build).
        var metadata = new List<object>();
        foreach (var attr in controllerTypeInfo.GetCustomAttributes(inherit: true))
        {
            metadata.Add(attr);
        }
        foreach (var attr in methodInfo.GetCustomAttributes(inherit: true))
        {
            metadata.Add(attr);
        }
        descriptor.EndpointMetadata = metadata;

        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "test");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        httpContext.Request.Method = httpMethod;

        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    /// <summary>
    /// Builds a filter with a service mock that returns the supplied boolean.
    /// </summary>
    private static DynamicAuthorizationFilter CreateFilter(out Mock<IPermissionService> serviceMock, bool hasAccess = true)
    {
        serviceMock = new Mock<IPermissionService>();
        serviceMock
            .Setup(s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PermissionFlags?>()))
            .ReturnsAsync(hasAccess);

        return new DynamicAuthorizationFilter(serviceMock.Object, Options.Create(new ModuleEnforcementSettings()));
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenAllowAnonymousAttributePresent_ShouldSkipAndNotCallService()
    {
        var ctx = BuildContext(typeof(AnonymousController), "Get");
        var filter = CreateFilter(out var serviceMock);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeNull();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenSkipDynamicAuthorizationAttributePresent_ShouldSkipAndNotCallService()
    {
        var ctx = BuildContext(typeof(SkipController), "Get");
        var filter = CreateFilter(out var serviceMock);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeNull();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenSuperAdminRole_ShouldSkipAndNotCallService()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "List",
            claims: new[] { (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), (ClaimTypes.Role, SuperAdminRoleName) });

        var filter = CreateFilter(out var serviceMock, hasAccess: false);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeNull();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenMissingUserId_ShouldReturn401()
    {
        var ctx = BuildContext(typeof(PersonsController), "List");

        var filter = CreateFilter(out var serviceMock);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedResult>();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenMissingCompanyId_ShouldReturn401()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "List",
            claims: new[] { (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) });

        var filter = CreateFilter(out var serviceMock);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeOfType<UnauthorizedResult>();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenPermissionGranted_ShouldNotSetResult()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "List",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeNull();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenPermissionDenied_ShouldReturn403()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "List",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: false);

        await filter.OnAuthorizationAsync(ctx);

        ctx.Result.Should().BeOfType<ForbidResult>();
        serviceMock.Verify(
            s => s.HasPermissionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenRequireExportOnGet_ShouldPassCanExportFlag()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "Export",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "GET",
                PermissionFlags.CanExport),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenRequireExecuteOnPost_ShouldPassCanExecuteFlag()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "Run",
            httpMethod: "POST",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "POST",
                PermissionFlags.CanExecute),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenNoResourceAttribute_ShouldInferFromControllerName()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "List",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "Persons",
                It.IsAny<string>(),
                It.IsAny<PermissionFlags?>()),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenActionHasPermissionResource_ShouldUseActionResource()
    {
        var ctx = BuildContext(
            typeof(PersonsController), "WithExplicitResource",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "ExplicitResource",
                It.IsAny<string>(),
                It.IsAny<PermissionFlags?>()),
            Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenClassHasPermissionResource_ShouldUseClassResource()
    {
        var ctx = BuildContext(
            typeof(ClassResourceController), "Get",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                "ClassResource",
                It.IsAny<string>(),
                It.IsAny<PermissionFlags?>()),
            Times.Once);
    }

    // ── SPEC 48 module gate ──────────────────────────────────────────────────

    /// <summary>
    /// Builds a filter where <c>EnforceCompanyModules</c> is true and the service
    /// reports the resource's module as enabled. The HasPermissionAsync path is exercised
    /// after the module gate (when the gate lets the request through).
    /// </summary>
    private static DynamicAuthorizationFilter CreateEnforcedModuleFilter(
        out Mock<IPermissionService> serviceMock,
        bool moduleEnabled = true,
        bool hasAccess = true)
    {
        serviceMock = new Mock<IPermissionService>();
        serviceMock
            .Setup(s => s.GetModuleAccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ModuleAccessResult(moduleEnabled, "Tickets"));
        serviceMock
            .Setup(s => s.HasPermissionAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<PermissionFlags?>()))
            .ReturnsAsync(hasAccess);

        return new DynamicAuthorizationFilter(
            serviceMock.Object,
            Options.Create(new ModuleEnforcementSettings { EnforceCompanyModules = true }));
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenEnforceCompanyModulesOff_ShouldNotCallModuleGate()
    {
        var ctx = BuildContext(
            typeof(PersonsController),
            "List",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateFilter(out var serviceMock, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.GetModuleAccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenEnforceCompanyModulesOn_AndModuleEnabled_ShouldCallPermissionService()
    {
        var ctx = BuildContext(
            typeof(PersonsController),
            "List",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateEnforcedModuleFilter(out var serviceMock, moduleEnabled: true, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.GetModuleAccessAsync(It.IsAny<Guid>(), "Persons", It.IsAny<CancellationToken>()),
            Times.Once);
        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), "Persons", "GET", It.IsAny<PermissionFlags?>()),
            Times.Once);
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenEnforceCompanyModulesOn_AndModuleDisabled_ShouldReturn403WithModuleNotEnabledCode()
    {
        var ctx = BuildContext(
            typeof(PersonsController),
            "List",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateEnforcedModuleFilter(out var serviceMock, moduleEnabled: false, hasAccess: true);

        await filter.OnAuthorizationAsync(ctx);

        var result = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        result.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(StatusCodes.Status403Forbidden);
        problem.Extensions.Should().ContainKey("code").WhoseValue.Should().Be("MODULE_NOT_ENABLED");
        problem.Extensions.Should().ContainKey("module").WhoseValue.Should().Be("Tickets");

        // HasPermissionAsync must NOT run — the module gate fires first.
        serviceMock.Verify(
            s => s.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PermissionFlags?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenEnforceCompanyModulesOn_AndSuperAdmin_ShouldSkipModuleGate()
    {
        var ctx = BuildContext(
            typeof(PersonsController),
            "List",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                (ClaimTypes.Role, SuperAdminRoleName)
            });

        var filter = CreateEnforcedModuleFilter(out var serviceMock, moduleEnabled: false);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.GetModuleAccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ctx.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhenEnforceCompanyModulesOn_AndSkipDynamicAuthorization_ShouldSkipModuleGate()
    {
        var ctx = BuildContext(
            typeof(SkipController),
            "Get",
            httpMethod: "GET",
            claims: new[]
            {
                (ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                ("CompanyId", Guid.NewGuid().ToString())
            });

        var filter = CreateEnforcedModuleFilter(out var serviceMock, moduleEnabled: false);

        await filter.OnAuthorizationAsync(ctx);

        serviceMock.Verify(
            s => s.GetModuleAccessAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        ctx.Result.Should().BeNull();
    }
}
