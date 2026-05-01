using System;

namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for paged listings of role-system-option permissions.
/// </summary>
public sealed record RoleSystemOptionListItemDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid SystemOptionId { get; init; }
    public string SystemOptionName { get; init; } = string.Empty;
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public DateTime Created { get; init; }
}
