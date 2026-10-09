using JOIN.Application.DTO.Common;

// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Lightweight projection of a RoleCompany row for paged listings.
/// </summary>
public sealed record RoleCompanyListItemDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public bool IsSystemDefault { get; init; }
    public DateTime Created { get; init; }
}
