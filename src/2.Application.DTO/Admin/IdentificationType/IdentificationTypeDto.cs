using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing an identification document type.
/// </summary>
public sealed record IdentificationTypeDto
{
    /// <summary>
    /// Gets the unique identifier of the identification type.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the identification type.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the detailed description of the identification type.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the optional validation pattern associated with the identification type.
    /// </summary>
    public string? ValidationPattern { get; init; }

    /// <summary>
    /// Gets a value indicating whether the identification type is currently active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}