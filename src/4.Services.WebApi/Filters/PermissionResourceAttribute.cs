


namespace JOIN.Services.WebApi.Filters;



/// <summary>
/// Declares the explicit permission resource name associated with a controller or action.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionResourceAttribute(string resourceName) : Attribute
{
    public string ResourceName { get; } = resourceName;
}