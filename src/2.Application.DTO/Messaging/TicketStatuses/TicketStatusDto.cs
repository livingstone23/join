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
    /// Gets the company identifier that owns the ticket status.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the company display name that owns the ticket status.
    /// </summary>
    public string? CompanyName { get; init; }

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
    /// Gets whether the ticket status is configured as the initial workflow status.
    /// </summary>
    public bool IsInitial { get; init; }

    /// <summary>
    /// Gets whether the ticket status is configured as the paused workflow status.
    /// </summary>
    public bool IsPaused { get; init; }

    /// <summary>
    /// Gets whether the ticket status is configured as the final workflow status.
    /// </summary>
    public bool IsFinal { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
