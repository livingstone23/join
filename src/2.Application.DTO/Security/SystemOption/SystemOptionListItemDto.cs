using System;

namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for listing system options in paginated results.
/// </summary>
public sealed record SystemOptionListItemDto
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public DateTime Created { get; init; }
}
