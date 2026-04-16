using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) that represents a flattened ticket audit log entry.
/// </summary>
public sealed record TicketLogDto
{
    /// <summary>
    /// Gets the log identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the log type display value.
    /// </summary>
    public string LogType { get; init; } = string.Empty;

    /// <summary>
    /// Gets the event summary.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the display name of the user who registered the log entry.
    /// </summary>
    public string UserRegisteredName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional previous status name.
    /// </summary>
    public string? PreviousStatusName { get; init; }

    /// <summary>
    /// Gets the current status name after the change.
    /// </summary>
    public string NewStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional consumed time associated with the log entry.
    /// </summary>
    public decimal? ConsumedTime { get; init; }
}
