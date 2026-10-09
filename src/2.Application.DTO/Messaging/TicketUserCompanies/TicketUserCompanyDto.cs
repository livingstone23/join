using System;

using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Data Transfer Object (DTO) representing the link between a user and a company
/// inside the ticket-management roster, with the user's ticket capabilities.
/// </summary>
public record TicketUserCompanyDto : SoftDeletableDto
{
    /// <summary>
    /// Gets the unique identifier of the row.
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
    /// Gets the linked user's identifier.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the linked user's display name (first + last name).
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the linked user's email.
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Gets whether this user can redistribute (reassign) any ticket in the company.
    /// </summary>
    public bool IsSuperAdminTicket { get; init; }

    /// <summary>
    /// Gets whether this user can mark a ticket as finished.
    /// </summary>
    public bool CanFinishTicket { get; init; }

    /// <summary>
    /// Gets whether this user is eligible to receive and manage ticket assignments.
    /// </summary>
    public bool CanResolveTicket { get; init; }

    /// <summary>
    /// Gets the UTC creation timestamp of the record.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
