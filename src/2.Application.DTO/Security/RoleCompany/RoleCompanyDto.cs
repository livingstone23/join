// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Detailed data transfer object for a RoleCompany junction.
/// CompanyId is intentionally omitted: it always comes from the caller's token.
/// </summary>
public sealed record RoleCompanyDto
{
    public Guid Id { get; init; }
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public bool IsSystemDefault { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime Created { get; init; }
}
