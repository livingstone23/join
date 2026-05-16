using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace JOIN.Application.Common;

/// <summary>
/// Helpers for resolving enum display metadata.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Resolves the <see cref="DisplayAttribute.Name"/> value for an enum member, or the member name when absent.
    /// </summary>
    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        var attribute = member?.GetCustomAttribute<DisplayAttribute>();
        return attribute?.Name ?? value.ToString();
    }
}
