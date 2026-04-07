namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents a user projection together with the list of roles currently assigned to that account.
/// This DTO is used by administrative user-management endpoints that need to display or confirm role membership before changes are applied.
/// </summary>
public record UserWithRolesDto
{
    /// <summary>
    /// Gets the unique identifier of the user represented by the projection.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the application user name associated with the account.
    /// </summary>
    public string UserName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the primary email address registered for the user account.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the user account is currently active in the system.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the set of role names currently assigned to the user.
    /// The collection is returned in a normalized, client-friendly shape for administrative views.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}
