using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a tenant-scoped project.
/// </summary>
public sealed record ProjectDto
{
    /// <summary>
    /// Gets the unique identifier of the project.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the project.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tenant identifier that owns the project.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company that owns the project.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the identifier of the associated entity status.
    /// </summary>
    public Guid EntityStatusId { get; init; }

    /// <summary>
    /// Gets the name of the associated entity status.
    /// </summary>
    public string EntityStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp of the project.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}