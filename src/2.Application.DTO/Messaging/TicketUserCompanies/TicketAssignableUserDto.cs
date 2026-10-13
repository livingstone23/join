namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Lightweight projection of a ticket-roster member that the active user may
/// assign a ticket to (SPEC 53). Same <c>UserName</c> shape as
/// <see cref="TicketUserCompanyDto.UserName"/> so the FE-17 "Asignar a" dropdown
/// can reuse its formatter without a second template.
/// </summary>
public record TicketAssignableUserDto
{
    /// <summary>
    /// Gets the assignable user's identifier.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the assignable user's display name (first + last name), matching
    /// <see cref="TicketUserCompanyDto.UserName"/>.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the assignable user's email.
    /// </summary>
    public string? UserEmail { get; init; }
}