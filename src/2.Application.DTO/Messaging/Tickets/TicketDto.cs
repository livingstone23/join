using System;
using System.Collections.Generic;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) that represents a ticket detail projection with flattened related data.
/// </summary>
public record TicketDto
{
    /// <summary>
    /// Gets the ticket identifier.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant company identifier.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the tenant company display name.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets the human-readable code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ticket title.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ticket description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets the estimated time value.
    /// </summary>
    public decimal EstimatedTime { get; init; }

    /// <summary>
    /// Gets the consumed time value.
    /// </summary>
    public decimal ConsumedTime { get; init; }

    /// <summary>
    /// Gets the optional effort points value.
    /// </summary>
    public decimal? EffortPoints { get; init; }

    /// <summary>
    /// Gets whether the ticket is visible to external users.
    /// </summary>
    public bool IsVisibleToExternals { get; init; }

    /// <summary>
    /// Gets the status identifier.
    /// </summary>
    public Guid TicketStatusId { get; init; }

    /// <summary>
    /// Gets the status name.
    /// </summary>
    public string TicketStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the complexity identifier.
    /// </summary>
    public Guid TicketComplexityId { get; init; }

    /// <summary>
    /// Gets the complexity name.
    /// </summary>
    public string TicketComplexityName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the time unit identifier.
    /// </summary>
    public Guid TimeUnitId { get; init; }

    /// <summary>
    /// Gets the time unit name.
    /// </summary>
    public string TimeUnitName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional customer identifier.
    /// </summary>
    public Guid? PersonId { get; init; }

    /// <summary>
    /// Gets the optional customer display name.
    /// </summary>
    public string? PersonName { get; init; }

    /// <summary>
    /// Gets the optional project identifier.
    /// </summary>
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Gets the optional project name.
    /// </summary>
    public string? ProjectName { get; init; }

    /// <summary>
    /// Gets the optional area identifier.
    /// </summary>
    public Guid? AreaId { get; init; }

    /// <summary>
    /// Gets the optional area name.
    /// </summary>
    public string? AreaName { get; init; }

    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string ChannelName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the creator user identifier.
    /// </summary>
    public Guid CreatedByUserId { get; init; }

    /// <summary>
    /// Gets the creator display name.
    /// </summary>
    public string CreatedByUserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional assigned user identifier.
    /// </summary>
    public Guid? AssignedToUserId { get; init; }

    /// <summary>
    /// Gets the optional assigned user display name.
    /// </summary>
    public string? AssignedToUserName { get; init; }

    /// <summary>
    /// Gets the optional precedent ticket identifier.
    /// </summary>
    public Guid? PrecedentTicketId { get; init; }

    /// <summary>
    /// Gets the optional precedent ticket code.
    /// </summary>
    public string? PrecedentTicketCode { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Gets the ticket audit history entries ordered by recency.
    /// </summary>
    public IEnumerable<TicketLogDto> Logs { get; set; } = new List<TicketLogDto>();
}
