// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;

namespace JOIN.Application.Common;

/// <summary>
/// Shared restore logic for logically deleted entities (SPEC 41, section B). Every
/// <c>Restore&lt;Entity&gt;CommandHandler</c> guards <c>CompanyId</c> and the <c>SuperAdmin</c>
/// role itself and then delegates here, passing the entity-specific parent and active-duplicate
/// checks. This is the only place allowed to call <see cref="IGenericRepository{T}.GetIncludingDeletedAsync"/>
/// for a restore, because that method bypasses every global query filter — including the
/// tenant one — and the tenant is validated here before the row is touched.
/// </summary>
public sealed class SoftDeleteRestorer(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
{
    /// <summary>
    /// Restores the entity <paramref name="id"/> by resetting its <c>GcRecord</c> to active.
    /// </summary>
    /// <param name="id">The identifier of the entity to restore.</param>
    /// <param name="requestedCompanyId">Optional tenant override, resolved through <see cref="TenantResolver"/>.</param>
    /// <param name="isParentDeleted">Returns <c>true</c> when a parent of the entity is still deleted.</param>
    /// <param name="activeDuplicateExists">Returns <c>true</c> when an active row already holds the entity's natural key.</param>
    /// <param name="beforeRestore">Optional step run after every check passed and right before the restore, e.g. to
    /// drop a default/primary/current flag another active row already holds (SPEC 41, Etapa 2).</param>
    /// <param name="restoreCascade">Optional step for composition parents (SPEC 41, decision 2026-10-08): receives the
    /// entity and its original <c>GcRecord</c> stamp, restores the children deleted in the same cascade and returns
    /// <c>null</c>, or an error response that aborts the whole restore (nothing is saved).</param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>The restored entity id, or <c>NOT_FOUND</c>, <c>NOT_DELETED</c>, <c>PARENT_DELETED</c>,
    /// <c>ACTIVE_DUPLICATE_EXISTS</c> or <c>RESTORE_FAILED</c>.</returns>
    public async Task<Response<Guid>> RestoreAsync<TEntity>(
        Guid id,
        Guid? requestedCompanyId,
        Func<TEntity, CancellationToken, Task<bool>>? isParentDeleted = null,
        Func<TEntity, CancellationToken, Task<bool>>? activeDuplicateExists = null,
        Func<TEntity, CancellationToken, Task>? beforeRestore = null,
        Func<TEntity, int, CancellationToken, Task<Response<Guid>?>>? restoreCascade = null,
        CancellationToken cancellationToken = default)
        where TEntity : BaseAuditableEntity
    {
        var entityName = typeof(TEntity).Name;
        var entity = await unitOfWork.GetRepository<TEntity>().GetIncludingDeletedAsync(id);

        if (entity is null)
        {
            return Response<Guid>.Error("NOT_FOUND", [$"{entityName} '{id}' was not found."]);
        }

        // A row from another tenant answers NOT_FOUND (not FORBIDDEN) so the caller cannot
        // confirm that a record exists in a company they did not resolve to.
        if (entity is BaseTenantEntity tenantEntity
            && tenantEntity.CompanyId != TenantResolver.Resolve(currentUserService, requestedCompanyId))
        {
            return Response<Guid>.Error("NOT_FOUND", [$"{entityName} '{id}' was not found."]);
        }

        if (!entity.IsDeleted)
        {
            return Response<Guid>.Error("NOT_DELETED", [$"{entityName} '{id}' is not deleted."]);
        }

        if (isParentDeleted is not null && await isParentDeleted(entity, cancellationToken))
        {
            return Response<Guid>.Error(
                "PARENT_DELETED",
                [$"{entityName} '{id}' cannot be restored while its parent is deleted. Restore the parent first."]);
        }

        if (activeDuplicateExists is not null && await activeDuplicateExists(entity, cancellationToken))
        {
            return Response<Guid>.Error(
                "ACTIVE_DUPLICATE_EXISTS",
                [$"An active {entityName} with the same unique key already exists."]);
        }

        if (beforeRestore is not null)
        {
            await beforeRestore(entity, cancellationToken);
        }

        if (restoreCascade is not null)
        {
            var cascadeError = await restoreCascade(entity, entity.GcRecord, cancellationToken);
            if (cascadeError is not null)
            {
                return cascadeError;
            }
        }

        entity.Restore();

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("RESTORE_FAILED", [$"No records were affected while restoring {entityName} '{id}'."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Data = entity.Id,
            Message = $"{entityName} restored successfully."
        };
    }

    /// <summary>
    /// Returns <c>true</c> when the parent <paramref name="parentId"/> is logically deleted or no longer
    /// exists. Used by the <c>isParentDeleted</c> checks: a child is never restored before its parent.
    /// </summary>
    public async Task<bool> IsParentDeletedAsync<TParent>(Guid parentId)
        where TParent : class, IAuditableEntity
    {
        // IAuditableEntity (not BaseAuditableEntity) so Identity parents such as ApplicationUser qualify.
        var parent = await unitOfWork.GetRepository<TParent>().GetIncludingDeletedAsync(parentId);
        return parent is null || parent.GcRecord != BaseAuditableEntity.ActiveGcRecord;
    }

    /// <summary>
    /// Returns <c>true</c> when at least one row matches <paramref name="predicate"/>, across every
    /// tenant and including deleted rows (the predicate must filter <c>GcRecord == 0</c> itself). Used
    /// by the <c>activeDuplicateExists</c> checks against the natural key of the filtered unique index.
    /// </summary>
    public async Task<bool> AnyAsync<TEntity>(Expression<Func<TEntity, bool>> predicate)
        where TEntity : class
        => (await unitOfWork.GetRepository<TEntity>().GetAllIncludingDeletedAsync(predicate)).Any();
}
