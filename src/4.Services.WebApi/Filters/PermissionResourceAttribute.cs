namespace JOIN.Services.WebApi.Filters;

/// <summary>
/// Declares the permission resource name associated with a controller or action.
/// When applied without arguments, the resource name is inferred from the controller
/// class name (with the "Controller" suffix stripped) by the authorization filter.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionResourceAttribute : Attribute
{
    public string? ResourceName { get; }

    /// <summary>
    /// Resource name is provided explicitly (backward compatible with pre-SPEC 17 usage).
    /// </summary>
    public PermissionResourceAttribute(string resourceName)
    {
        ResourceName = resourceName;
    }

    /// <summary>
    /// Resource name will be inferred from the controller class name at runtime.
    /// </summary>
    public PermissionResourceAttribute()
    {
        ResourceName = null;
    }
}
