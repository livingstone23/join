// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Tracks the last checksum of the menu/permissions seed applied for a company,
/// so the idempotent Development reseed can be skipped when nothing changed.
/// </summary>
public class SeedState : BaseEntity
{
    /// <summary>
    /// The tenant the seed was applied for. Unique per row — one entry per company.
    /// </summary>
    public Guid CompanyId { get; set; }

    /// <summary>
    /// SHA-256 hash of the current in-code seed definitions (roles, users, system options,
    /// role/system-option permissions) used by SeedMenuAndPermissionsAsync.
    /// </summary>
    public string Checksum { get; set; } = string.Empty;

    /// <summary>
    /// UTC timestamp of when the seed was last applied successfully for this company.
    /// </summary>
    public DateTime LastAppliedAt { get; set; }
}
