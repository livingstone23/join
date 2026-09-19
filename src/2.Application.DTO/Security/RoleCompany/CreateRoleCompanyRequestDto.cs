// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Request payload for creating a new RoleCompany link.
/// <see cref="CompanyId"/> is honored only when the caller is a real <c>SuperAdmin</c> (Identity role);
/// for anyone else (including <c>SuperAdminCompany</c>) it is ignored and resolved from the token,
/// same as before (join_frontb specs/10-role-companies.md).
/// </summary>
public sealed record CreateRoleCompanyRequestDto(Guid RoleId, Guid? CompanyId = null);
