using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a street type catalog item.
/// </summary>
public record StreetTypeDto
{
    /// <summary>
    /// Gets the street type identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the street type name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the street type abbreviation.
    /// </summary>
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>
    /// Gets whether the street type is active.
    /// </summary>
    public bool IsActive { get; init; }
}
