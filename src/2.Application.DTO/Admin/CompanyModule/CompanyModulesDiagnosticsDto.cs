namespace JOIN.Application.DTO.Admin;

/// <summary>
/// SPEC 48 F: pre-flight snapshot that lets a SuperAdmin enumerate companies whose
/// <c>Admin.CompanyModules</c> assignments would be impacted by flipping the
/// <c>Modules:EnforceCompanyModules</c> switch. Companies without problems are omitted.
/// </summary>
public sealed record CompanyModulesDiagnosticsDto
{
    /// <summary>
    /// Current value of <c>Modules:EnforceCompanyModules</c>. The endpoint is informational:
    /// it does not flip the switch.
    /// </summary>
    public bool EnforceCompanyModules { get; init; }

    /// <summary>
    /// Names of the modules flagged as <c>IsBase = TRUE</c> in <c>Admin.SystemModules</c>.
    /// Every company must have an active <c>CompanyModule</c> for each of these.
    /// </summary>
    public IReadOnlyCollection<string> BaseModules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Active companies with at least one missing base module, one or more inactive
    /// <c>CompanyModules</c>, or both. Empty when the deployment is safe to enable.
    /// </summary>
    public IReadOnlyCollection<CompanyDiagnosticsEntry> Companies { get; init; } = Array.Empty<CompanyDiagnosticsEntry>();
}

/// <summary>
/// One row of <see cref="CompanyModulesDiagnosticsDto.Companies"/>.
/// </summary>
public sealed record CompanyDiagnosticsEntry
{
    /// <summary>Active company identifier.</summary>
    public Guid CompanyId { get; init; }

    /// <summary>Company display name.</summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Names of <c>IsBase = TRUE</c> modules that this company has no active
    /// <c>CompanyModule</c> row for. Empty when the company is complete.
    /// </summary>
    public IReadOnlyCollection<string> MissingBaseModules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Count of distinct users in this company who currently hold role permissions on
    /// <c>SystemOptions</c> whose owning module is not active for the company. These users
    /// would start receiving <c>MODULE_NOT_ENABLED</c> when the switch flips to true.
    /// </summary>
    public int UsersWithPermissionsInDisabledModules { get; init; }
}