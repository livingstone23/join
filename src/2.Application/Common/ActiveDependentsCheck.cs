// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using JOIN.Application.Interface.Persistence;

namespace JOIN.Application.Common;

/// <summary>
/// Collects the active children that block the logical delete of a parent (SPEC 41, section E:
/// a parent with active children cannot be deleted). Each <c>Delete&lt;Entity&gt;CommandHandler</c>
/// counts its children and, when <see cref="HasDependents"/> is <c>true</c>, returns
/// <c>&lt;ENTITY&gt;_IN_USE</c> with <see cref="Details"/> as the errors.
/// <para>
/// Counts go through <see cref="IGenericRepository{T}.GetAllIncludingDeletedAsync"/>, which bypasses the
/// global query filters, so children of every tenant are counted (a global catalog such as
/// <c>Country</c> is referenced by every company) and the predicate is translated to SQL. The
/// predicate must therefore include <c>GcRecord == 0</c> itself.
/// </para>
/// </summary>
public sealed class ActiveDependentsCheck(IUnitOfWork unitOfWork)
{
    private readonly List<string> _details = [];

    /// <summary>
    /// Gets one entry per child type with active rows, e.g. <c>"Active provinces: 3"</c>.
    /// </summary>
    public IReadOnlyList<string> Details => _details;

    /// <summary>
    /// Gets whether any active child was found.
    /// </summary>
    public bool HasDependents => _details.Count > 0;

    /// <summary>
    /// Counts the active children matching <paramref name="activeChildPredicate"/> and records them
    /// under <paramref name="label"/> when there is at least one.
    /// </summary>
    /// <param name="activeChildPredicate">Predicate over the child, including <c>GcRecord == 0</c>.</param>
    /// <param name="label">Plural, lower-case child name used in the detail, e.g. <c>"provinces"</c>.</param>
    public async Task<ActiveDependentsCheck> CountAsync<TChild>(
        Expression<Func<TChild, bool>> activeChildPredicate,
        string label)
        where TChild : class
    {
        var count = (await unitOfWork.GetRepository<TChild>().GetAllIncludingDeletedAsync(activeChildPredicate)).Count();
        if (count > 0)
        {
            _details.Add($"Active {label}: {count}");
        }

        return this;
    }
}
