using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) representing a ticket status catalog item.
/// </summary>
public record TicketStatusDto
{
    /// <summary>
    /// Gets the unique identifier of the ticket status.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the display name of the ticket status.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional description of the ticket status.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the numeric code associated with the ticket status.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets whether the ticket status is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
