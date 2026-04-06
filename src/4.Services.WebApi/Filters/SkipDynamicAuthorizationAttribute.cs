using System;

namespace JOIN.Services.WebApi.Filters;

/// <summary>
/// Marks endpoints that should require authentication but skip the controller-based dynamic permission filter.
/// Useful for endpoints that are responsible for resolving the permission-driven navigation itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SkipDynamicAuthorizationAttribute : Attribute
{
}
