using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) used for paginated identification type list responses.
/// </summary>
public sealed record IdentificationTypeListItemDto
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
    /// Gets a value indicating whether the identification type is currently active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}