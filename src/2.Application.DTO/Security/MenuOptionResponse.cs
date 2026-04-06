namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents a sidebar menu option resolved for the authenticated user.
/// </summary>
public sealed record MenuOptionResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? ControllerName { get; init; }
    public Guid? ParentId { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public List<MenuOptionResponse> Children { get; init; } = [];
}
