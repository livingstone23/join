// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Defines which Roles are available within a specific Company (tenant).
/// A role can only be assigned to a user of Company X if there is an active
/// <see cref="RoleCompany"/> link for (Role, Company X). This is the catalog
/// a SuperAdminCompany uses to decide which roles their company may assign.
/// </summary>
public class RoleCompany : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the ApplicationRole.
    /// </summary>
    public Guid RoleId { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationRole Role { get; set; } = null!;
}
