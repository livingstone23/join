using System;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Data Transfer Object (DTO) used for paginated communication channel responses.
/// </summary>
public record CommunicationChannelListItemDto
{
    /// <summary>
    /// Gets the communication channel identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the channel provider.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// Gets the channel code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets whether the channel is active.
    /// </summary>
    public bool IsActive { get; init; }
}
