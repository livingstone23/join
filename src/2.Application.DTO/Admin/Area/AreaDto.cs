using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a tenant-scoped functional area.
/// </summary>
public record AreaDto
{
    /// <summary>
    /// Gets the unique identifier of the area.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the company that owns the area.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company that owns the area.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the business name of the area.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the foreign key associated with the current entity status.
    /// </summary>
    public Guid EntityStatusId { get; init; }

    /// <summary>
    /// Gets the display name of the current entity status.
    /// </summary>
    public string? EntityStatusName { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the area record.
    /// </summary>
    public DateTime Created { get; init; }
}
