using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) used for paginated entity status list responses.
/// </summary>
public sealed record EntityStatusListItemDto
{
    /// <summary>
    /// Gets the unique identifier of the entity status.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the entity status.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the detailed description of the entity status.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the internal numeric code of the entity status.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets a value indicating whether the status is operative.
    /// </summary>
    public bool IsOperative { get; init; }

    /// <summary>
    /// Gets a value indicating whether the status is active for API consumers.
    /// This mirrors <see cref="IsOperative"/>.
    /// </summary>
    public bool IsActive => IsOperative;

    /// <summary>
    /// Gets the UTC creation timestamp of the status record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
