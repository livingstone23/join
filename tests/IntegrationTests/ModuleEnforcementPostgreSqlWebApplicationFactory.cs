namespace JOIN.IntegrationTests;

/// <summary>
/// SPEC 48-post — <see cref="CustomWebApplicationFactory"/> with <c>Modules:EnforceCompanyModules = true</c>,
/// so the API gate and the sidebar filter by <c>CompanyModules</c> are on. The base factory keeps the
/// deployment default (switch off).
/// </summary>
public sealed class ModuleEnforcementPostgreSqlWebApplicationFactory : CustomWebApplicationFactory
{
    protected override IReadOnlyDictionary<string, string?> AdditionalConfiguration { get; } =
        new Dictionary<string, string?> { ["Modules:EnforceCompanyModules"] = "true" };
}
