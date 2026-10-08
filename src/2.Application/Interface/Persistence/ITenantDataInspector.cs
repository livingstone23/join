// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

namespace JOIN.Application.Interface.Persistence;

/// <summary>
/// Inspects the data a company still owns (SPEC 41, decision 2026-10-08: a company with active data
/// cannot be deleted).
/// </summary>
public interface ITenantDataInspector
{
    /// <summary>
    /// Counts the active rows (<c>GcRecord = 0</c>) of every entity that belongs to
    /// <paramref name="companyId"/>, across all tenant tables of the model.
    /// </summary>
    /// <returns>One detail per entity type with active rows, e.g. <c>"Active Person: 12"</c>; empty when none.</returns>
    Task<IReadOnlyList<string>> GetActiveDataAsync(Guid companyId, CancellationToken cancellationToken = default);
}
