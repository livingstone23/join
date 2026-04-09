using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) representing a time unit catalog item.
/// </summary>
public record TimeUnitDto
{
    /// <summary>
    /// Gets the unique identifier of the time unit.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the time unit.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the numeric code or multiplier of the time unit.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets whether the time unit is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
