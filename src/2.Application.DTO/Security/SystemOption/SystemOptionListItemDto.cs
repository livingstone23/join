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
    public string? Icon { get; init; }
    public string? ControllerName { get; init; }
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public bool CanDownload { get; init; }
    public bool CanExport { get; init; }
    public bool CanExecute { get; init; }
    public bool IsVisibleMenu { get; init; }
    public int? OrderMenu { get; init; }
    public DateTime Created { get; init; }
}
