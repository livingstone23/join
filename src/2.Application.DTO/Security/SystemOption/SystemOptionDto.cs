using System;

namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for detailed information about a system option.
/// </summary>
public sealed record SystemOptionDto
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentName { get; init; }
    public string? ControllerName { get; init; }
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public DateTime Created { get; init; }
}
