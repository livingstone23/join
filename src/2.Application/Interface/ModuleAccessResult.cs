namespace JOIN.Application.Interface;

/// <summary>
/// Result of <see cref="IPermissionService.GetModuleAccessAsync"/>: whether the modules that own a permission
/// resource are enabled for the company (active globally and active for that company), and the name of the
/// first owning module so the authorization filter can produce a precise error message.
/// </summary>
/// <param name="IsEnabled">
/// <c>true</c> when at least one of the resource's owning modules is active globally and has an active
/// (not soft-deleted) <c>CompanyModule</c> row for the company, or when the resource has no
/// <c>SystemOption</c> row at all (not module-scoped). <c>false</c> when none of its modules is enabled.
/// </param>
/// <param name="ModuleName">
/// Name of the first module that owns the resource, used to compose the <c>MODULE_NOT_ENABLED</c>
/// ProblemDetails detail when <paramref name="IsEnabled"/> is false. <c>null</c> when the resource
/// is not module-scoped.
/// </param>
public readonly record struct ModuleAccessResult(bool IsEnabled, string? ModuleName);
