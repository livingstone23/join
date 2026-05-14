using System;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) that represents a paginated ticket row with flattened related data.
/// </summary>
public record TicketListItemDto
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
    /// Gets the ticket code.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets the ticket title.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the related status identifier.
    /// </summary>
    public Guid TicketStatusId { get; init; }

    /// <summary>
    /// Gets the related status name.
    /// </summary>
    public string TicketStatusName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the related complexity identifier.
    /// </summary>
    public Guid TicketComplexityId { get; init; }

    /// <summary>
    /// Gets the related complexity name.
    /// </summary>
    public string TicketComplexityName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional customer identifier.
    /// </summary>
    public Guid? PersonId { get; init; }

    /// <summary>
    /// Gets the optional customer name.
    /// </summary>
    public string? PersonName { get; init; }

    /// <summary>
    /// Gets the optional assigned user identifier.
    /// </summary>
    public Guid? AssignedToUserId { get; init; }

    /// <summary>
    /// Gets the optional assigned user name.
    /// </summary>
    public string? AssignedToUserName { get; init; }

    /// <summary>
    /// Gets the creation date in UTC.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
