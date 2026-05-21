namespace JOIN.Application.DTO.Security.Workspaces;



/// <summary>
/// Represents a role assigned to the current user in a specific company context.
/// </summary>
public sealed record MyCompanyRoleDto
{
    /// <summary>
    /// Gets the unique identifier of the role assignment.
    /// </summary>
    public Guid RoleId { get; init; }

    /// <summary>
    /// Gets the display name of the role.
    /// </summary>
    public string RoleName { get; init; } = string.Empty;
}