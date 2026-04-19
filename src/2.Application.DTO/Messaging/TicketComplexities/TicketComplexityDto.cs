using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) representing a ticket complexity catalog item.
/// </summary>
public record TicketComplexityDto
{
    /// <summary>
    /// Gets the unique identifier of the ticket complexity.
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
    /// Gets the display name of the ticket complexity.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional description of the ticket complexity.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the numeric code associated with the ticket complexity.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets the amount of configured time units required to resolve a ticket with this complexity.
    /// </summary>
    public int ResolutionTimeUnits { get; init; }

    /// <summary>
    /// Gets the related time unit identifier.
    /// </summary>
    public Guid TimeUnitId { get; init; }

    /// <summary>
    /// Gets whether the ticket complexity is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
