using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a region catalog item.
/// </summary>
public record RegionDto
{
    /// <summary>
    /// Gets the unique identifier of the region.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the region display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional region code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets the parent country identifier.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Gets the parent country display name.
    /// </summary>
    public string CountryName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
