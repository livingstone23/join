namespace JOIN.Application.Common.Settings;

/// <summary>
/// Controls whether the system enforces <c>Admin.CompanyModules</c>: when true, a company can only see in the menu
/// and call the APIs of modules that are active for it (and active globally). When false, <c>CompanyModules</c> is
/// ignored for both menu and APIs (behavior before SPEC 48). SuperAdmin is never restricted.
/// Deploy with false, review <c>GET /CompanyModules/diagnostics</c>, then switch to true.
/// </summary>
public sealed class ModuleEnforcementSettings
{
    /// <summary>Configuration section name. Must match appsettings.</summary>
    public const string SectionName = "Modules";

    /// <summary>
    /// When true, the global authorization filter and the sidebar menu both gate access by
    /// <c>SystemModule.IsActive</c> and <c>CompanyModule.IsActive</c>. SuperAdmin always bypasses.
    /// </summary>
    public bool EnforceCompanyModules { get; init; }
}