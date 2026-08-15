using System;

namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for detailed role-based option permissions in a company.
/// </summary>
public sealed record RoleSystemOptionDto
{
    /// <summary>
    /// Gets the unique identifier of the permission rule.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant company identifier that owns this rule.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company (tenant) that owns this rule.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the role identifier associated with this rule.
    /// </summary>
    public Guid RoleId { get; init; }

    /// <summary>
    /// Gets the display name of the role associated with this rule.
    /// </summary>
    public string RoleName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the system option identifier associated with this rule.
    /// </summary>
    public Guid SystemOptionId { get; init; }

    /// <summary>
    /// Gets the display name of the target system option.
    /// </summary>
    public string SystemOptionName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether read permission is granted.
    /// </summary>
    public bool CanRead { get; init; }

    /// <summary>
    /// Gets a value indicating whether create permission is granted.
    /// </summary>
    public bool CanCreate { get; init; }

    /// <summary>
    /// Gets a value indicating whether update permission is granted.
    /// </summary>
    public bool CanUpdate { get; init; }

    /// <summary>
    /// Gets a value indicating whether delete permission is granted.
    /// </summary>
    public bool CanDelete { get; init; }

    /// <summary>
    /// Gets a value indicating whether download permission is granted.
    /// </summary>
    public bool CanDownload { get; init; }

    /// <summary>
    /// Gets a value indicating whether export permission is granted.
    /// </summary>
    public bool CanExport { get; init; }

    /// <summary>
    /// Gets a value indicating whether execute permission is granted.
    /// </summary>
    public bool CanExecute { get; init; }

    /// <summary>
    /// Gets a value indicating whether the option is visible in the menu UI.
    /// </summary>
    public bool IsVisibleMenu { get; init; }

    /// <summary>
    /// Gets the menu ordering index. Null when not explicitly set.
    /// </summary>
    public int? OrderMenu { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the rule was created.
    /// </summary>
    public DateTime Created { get; init; }
}
