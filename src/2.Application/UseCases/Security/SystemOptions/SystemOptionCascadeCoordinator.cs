// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Security.SystemOptions;

/// <summary>
/// Soft-delete cascade of the menu hierarchy <c>SystemModule → SystemOption → child SystemOption</c>
/// (SPEC 41, decision 2026-10-08): deleting a module or an option deletes its whole active subtree with the
/// same <c>GcRecord</c> stamp, and restoring it brings back the options deleted in that same cascade.
/// Role grants (<see cref="RoleSystemOption"/>) are references, not composition: an active grant anywhere in
/// the subtree blocks the delete.
/// </summary>
public sealed class SystemOptionCascadeCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns the active options below <paramref name="rootOptionId"/> (every level, root excluded), or every
    /// active option of the module when <paramref name="rootOptionId"/> is <c>null</c>. Rows are not tracked.
    /// </summary>
    public async Task<IReadOnlyList<SystemOption>> GetActiveSubtreeAsync(Guid moduleId, Guid? rootOptionId)
    {
        var options = await LoadModuleOptionsAsync(moduleId);
        if (rootOptionId is null)
        {
            return options.Where(o => o.GcRecord == BaseAuditableEntity.ActiveGcRecord).ToList();
        }

        return Descendants(options, rootOptionId.Value, o => o.GcRecord == BaseAuditableEntity.ActiveGcRecord);
    }

    /// <summary>
    /// Returns <c>"Active role system options: N"</c> when any of <paramref name="optionIds"/> is granted to a role.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetBlockingReferencesAsync(IReadOnlyCollection<Guid> optionIds)
    {
        var references = new ActiveDependentsCheck(unitOfWork);
        await references.CountAsync<RoleSystemOption>(ro => ro.GcRecord == 0 && optionIds.Contains(ro.SystemOptionId), "role system options");
        return references.Details;
    }

    /// <summary>
    /// Marks <paramref name="options"/> (as returned by <see cref="GetActiveSubtreeAsync"/>) as deleted.
    /// </summary>
    public async Task MarkAsDeletedAsync(IEnumerable<SystemOption> options, DateTime deletedAtUtc)
    {
        var repository = unitOfWork.GetRepository<SystemOption>();
        foreach (var option in options)
        {
            option.MarkAsDeleted(deletedAtUtc);
            await repository.UpdateAsync(option);
        }
    }

    /// <summary>
    /// Restores the options deleted in the same cascade (<c>GcRecord == stamp</c>): below
    /// <paramref name="rootOptionId"/>, or in the whole module when it is <c>null</c>. An option is only
    /// restored when its parent is active or restored in this same batch (a child never comes back under a
    /// deleted parent). Always succeeds: options of another cascade simply stay deleted.
    /// </summary>
    public async Task<Response<Guid>?> RestoreSubtreeAsync(Guid moduleId, Guid? rootOptionId, int stamp)
    {
        var options = await LoadModuleOptionsAsync(moduleId);
        var candidates = rootOptionId is null
            ? options.Where(o => o.GcRecord == stamp).ToList()
            : Descendants(options, rootOptionId.Value, o => o.GcRecord == stamp);

        var byId = options.ToDictionary(o => o.Id);
        var restorable = new HashSet<Guid>();
        bool added;
        do
        {
            added = false;
            foreach (var option in candidates.Where(o => !restorable.Contains(o.Id)))
            {
                var parentIsBack = option.ParentId is not { } parentId
                    || parentId == rootOptionId
                    || restorable.Contains(parentId)
                    || (byId.TryGetValue(parentId, out var parent) && parent.GcRecord == BaseAuditableEntity.ActiveGcRecord);
                if (parentIsBack)
                {
                    restorable.Add(option.Id);
                    added = true;
                }
            }
        }
        while (added);

        var repository = unitOfWork.GetRepository<SystemOption>();
        foreach (var option in candidates.Where(o => restorable.Contains(o.Id)))
        {
            option.Restore();
            await repository.UpdateAsync(option);
        }

        return null;
    }

    private async Task<IReadOnlyList<SystemOption>> LoadModuleOptionsAsync(Guid moduleId)
        => (await unitOfWork.GetRepository<SystemOption>().GetAllIncludingDeletedAsync(o => o.ModuleId == moduleId)).ToList();

    /// <summary>
    /// Walks the tree below <paramref name="rootId"/> through the options that satisfy <paramref name="include"/>.
    /// </summary>
    private static List<SystemOption> Descendants(IReadOnlyList<SystemOption> options, Guid rootId, Func<SystemOption, bool> include)
    {
        var result = new List<SystemOption>();
        var pending = new Queue<Guid>([rootId]);
        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            foreach (var child in options.Where(o => o.ParentId == parentId && o.Id != rootId && include(o)))
            {
                result.Add(child);
                pending.Enqueue(child.Id);
            }
        }

        return result;
    }
}
