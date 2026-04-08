using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a module assignment for a tenant company.
/// </summary>
public sealed record CompanyModuleDto
{
    /// <summary>
    /// Gets the unique identifier of the company module assignment.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the company that owns the module assignment.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company that owns the module assignment.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the unique identifier of the assigned system module.
    /// </summary>
    public Guid ModuleId { get; init; }

    /// <summary>
    /// Gets the display name of the assigned system module.
    /// </summary>
    public string ModuleName { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the module is currently active for the company.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the assignment record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
