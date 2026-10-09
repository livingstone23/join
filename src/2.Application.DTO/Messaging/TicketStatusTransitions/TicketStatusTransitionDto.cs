using System;

using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) for a single allowed transition between two
/// <c>TicketStatus</c> values inside a tenant's ticket workflow.
/// </summary>
public record TicketStatusTransitionDto : SoftDeletableDto
{
    /// <summary>
    /// Gets the unique identifier of the transition rule.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the source status identifier.
    /// </summary>
    public Guid FromStatusId { get; init; }

    /// <summary>
    /// Gets the source status display name.
    /// </summary>
    public string FromStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the destination status identifier.
    /// </summary>
    public Guid ToStatusId { get; init; }

    /// <summary>
    /// Gets the destination status display name.
    /// </summary>
    public string ToStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC creation timestamp of the rule.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}