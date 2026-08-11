// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleUsers;

/// <summary>
/// Projection of a user for the "usuarios afectados" preview before saving role changes.
/// Tenant scoping is enforced upstream via the JWT CompanyId claim/header.
/// </summary>
public sealed record RoleAffectedUserDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public DateTime Created { get; init; }
    public bool IsSuperAdmin { get; init; }
    public bool IsSuperAdminCompany { get; init; }
    public bool EmailConfirmed { get; init; }
}
