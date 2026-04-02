using System;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object (DTO) representing a customer contact method.
/// </summary>
public record CustomerContactDto
{
    /// <summary>
    /// Global unique identifier for the contact record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Contact category (e.g., Mobile, Email, WhatsApp).
    /// </summary>
    public string ContactType { get; init; } = string.Empty;

    /// <summary>
    /// Contact information value (phone, email, etc.).
    /// </summary>
    public string ContactValue { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether this is the primary contact method.
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// Optional comments or instructions about the contact.
    /// </summary>
    public string? Comments { get; init; }

    /// <summary>
    /// Contact creation timestamp formatted as yyyy-MM-dd HH:mm.
    /// </summary>
    public string CreatedAt { get; init; } = string.Empty;
}
