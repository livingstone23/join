namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents an application user and the list of roles currently assigned.
/// </summary>
public record UserWithRolesDto
{
    public Guid Id { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
}
