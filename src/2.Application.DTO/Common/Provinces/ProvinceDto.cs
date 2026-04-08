using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) representing a province catalog item.
/// </summary>
public record ProvinceDto
{
    /// <summary>
    /// Gets the unique identifier of the province.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the province display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the province code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets the parent country identifier.
    /// </summary>
    public Guid CountryId { get; init; }

    /// <summary>
    /// Gets the parent country display name.
    /// </summary>
    public string CountryName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional parent region identifier.
    /// </summary>
    public Guid? RegionId { get; init; }

    /// <summary>
    /// Gets the optional parent region display name.
    /// </summary>
    public string? RegionName { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}