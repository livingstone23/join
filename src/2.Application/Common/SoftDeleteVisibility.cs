// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;

namespace JOIN.Application.Common;

/// <summary>
/// Resolves whether a query may include logically deleted rows (<c>GcRecord &gt; 0</c>).
/// Same rule style as <see cref="TenantResolver"/>: only a real <c>SuperAdmin</c> (Identity
/// role, not the <c>SuperAdminCompany</c> domain flag) who explicitly asks for it sees deleted
/// rows. For every other caller the request flag is silently ignored, so a request can never
/// widen what the token allows (SPEC 41). Query handlers must read <c>IncludeDeleted</c>
/// through this method, never straight from the request.
/// </summary>
public static class SoftDeleteVisibility
{
    public static bool IncludeDeleted(ICurrentUserService currentUserService, bool? requested) =>
        requested == true && currentUserService.IsInRole("SuperAdmin");
}
