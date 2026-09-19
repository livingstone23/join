// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Request payload for updating an existing RoleCompany link (changes RoleId; <see cref="CompanyId"/>
/// is honored only when the caller is a real <c>SuperAdmin</c> — otherwise ignored and resolved
/// from the token, same as before, join_frontb specs/10-role-companies.md).
/// </summary>
public sealed record UpdateRoleCompanyRequestDto(Guid RoleId, Guid? CompanyId = null);
