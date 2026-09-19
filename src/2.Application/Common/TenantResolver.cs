// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;

namespace JOIN.Application.Common;

/// <summary>
/// Resolves the effective tenant (CompanyId) for a request that may carry an explicit
/// <c>CompanyId</c> override. Only a real <c>SuperAdmin</c> (Identity role, not the
/// <c>SuperAdminCompany</c> domain flag) may cross tenants — everyone else always
/// resolves to their own token's <see cref="ICurrentUserService.CompanyId"/>, regardless
/// of what the request carries. See join_frontb specs/10-role-companies.md,
/// "Resolución de tenant dentro de cada handler".
/// </summary>
public static class TenantResolver
{
    public static Guid Resolve(ICurrentUserService currentUserService, Guid? requestedCompanyId) =>
        currentUserService.IsInRole("SuperAdmin") && requestedCompanyId is { } explicitCompanyId
            ? explicitCompanyId
            : currentUserService.CompanyId;
}
