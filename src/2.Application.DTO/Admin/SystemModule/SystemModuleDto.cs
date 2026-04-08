using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a system module.
/// </summary>
public sealed record SystemModuleDto
{
    /// <summary>
    /// Gets the unique identifier of the system module.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the system module.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the detailed description of the system module.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the icon identifier associated with the module.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Gets a value indicating whether the module is globally active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the module.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}