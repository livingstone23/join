// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using System.Reflection;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Persistence.Repositories.Common;

/// <summary>
/// EF Core implementation of <see cref="ITenantDataInspector"/>. It walks the model instead of keeping a
/// list, so every entity with a <c>CompanyId</c> and a <c>GcRecord</c> column — including the ones added in
/// future specs — blocks the delete of its company while it has active rows. Global query filters are
/// ignored on purpose: the caller (a SuperAdmin) may be scoped to another company.
/// </summary>
public sealed class TenantDataInspector(ApplicationDbContext context) : ITenantDataInspector
{
    private static readonly MethodInfo CountMethod =
        typeof(TenantDataInspector).GetMethod(nameof(CountActiveAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public async Task<IReadOnlyList<string>> GetActiveDataAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var details = new List<string>();
        var tenantTypes = context.Model.GetEntityTypes()
            .Where(entityType => !entityType.IsOwned()
                && entityType.ClrType != typeof(Company)
                && entityType.FindProperty("CompanyId")?.ClrType == typeof(Guid)
                && entityType.FindProperty("GcRecord")?.ClrType == typeof(int))
            .Select(entityType => entityType.ClrType)
            .Distinct()
            .OrderBy(type => type.Name);

        foreach (var type in tenantTypes)
        {
            var count = await (Task<int>)CountMethod.MakeGenericMethod(type).Invoke(this, [companyId, cancellationToken])!;
            if (count > 0)
            {
                details.Add($"Active {type.Name}: {count}");
            }
        }

        return details;
    }

    private Task<int> CountActiveAsync<T>(Guid companyId, CancellationToken cancellationToken)
        where T : class
        => context.Set<T>()
            .IgnoreQueryFilters()
            .CountAsync(
                entity => EF.Property<Guid>(entity, "CompanyId") == companyId && EF.Property<int>(entity, "GcRecord") == 0,
                cancellationToken);
}
