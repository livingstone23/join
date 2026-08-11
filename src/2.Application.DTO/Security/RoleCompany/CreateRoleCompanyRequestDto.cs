// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.DTO.Security.RoleCompany;

/// <summary>
/// Request payload for creating a new RoleCompany link.
/// CompanyId is resolved exclusively from the caller's token, never from the body.
/// </summary>
public sealed record CreateRoleCompanyRequestDto(Guid RoleId);
