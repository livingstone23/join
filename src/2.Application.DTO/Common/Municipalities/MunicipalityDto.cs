using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a municipality catalog item.
/// </summary>
public record MunicipalityDto
{
    /// <summary>
    /// Gets the unique identifier of the municipality.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the municipality display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional municipality code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets the parent province identifier.
    /// </summary>
    public Guid ProvinceId { get; init; }

    /// <summary>
    /// Gets the parent province display name.
    /// </summary>
    public string ProvinceName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
