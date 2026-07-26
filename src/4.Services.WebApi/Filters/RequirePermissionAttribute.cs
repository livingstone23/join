using JOIN.Domain.Security;

namespace JOIN.Services.WebApi.Filters;

/// <summary>
/// Overrides the default HTTP-verb-derived permission flag for a single action.
/// Use when an endpoint maps to a flag other than the one its HTTP method implies
/// (e.g. <c>GET /export</c> requires <see cref="PermissionFlags.CanExport"/>, not <see cref="PermissionFlags.CanRead"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(PermissionFlags flag) : Attribute
{
    public PermissionFlags Flag { get; } = flag;
}
