// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Request payload for updating an existing RoleCompany link (changes RoleId only;
/// CompanyId always comes from the caller's token).
/// </summary>
public sealed record UpdateRoleCompanyRequestDto(Guid RoleId);
