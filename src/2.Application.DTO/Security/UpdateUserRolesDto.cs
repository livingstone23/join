namespace JOIN.Application.DTO.Security;

/// <summary>
/// Request payload used to replace all roles assigned to a user.
/// </summary>
public record UpdateUserRolesDto
{
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}
