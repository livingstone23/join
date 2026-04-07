namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents the HTTP payload used to replace the complete set of roles assigned to a user account.
/// The list supplied by the client is interpreted as the final desired role state for the target user.
/// </summary>
public record UpdateUserRolesDto
{
    /// <summary>
    /// Gets the role names that must remain assigned to the user after the replacement operation completes.
    /// Roles omitted from this list are candidates to be removed by the corresponding command handler.
    /// </summary>
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}
