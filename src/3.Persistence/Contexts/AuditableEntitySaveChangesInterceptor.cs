// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;



namespace JOIN.Persistence.Contexts;



/// <summary>
/// Interceptor that automates Auditing (Created/Modified) and Multi-tenancy (CompanyId).
/// It also enforces Soft Delete by intercepting 'Deleted' states.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;

    public AuditableEntitySaveChangesInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        var currentUserId = _currentUserService.UserId ?? "System";
        var companyId = _currentUserService.CompanyId;
        var utcNow = DateTime.UtcNow;

        // We iterate through all entities inheriting from BaseAuditableEntity
        foreach (var entry in context.ChangeTracker.Entries<BaseAuditableEntity>())
        {
            // 1. Handle General Audit Fields
            if (entry.State == EntityState.Added)
            {
                entry.Entity.Created = utcNow;
                entry.Entity.CreatedBy = currentUserId;
                entry.Entity.GcRecord = 0; // Ensure it's active
            }

            if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.HasChangedOwnedEntities())
            {
                entry.Entity.LastModified = utcNow;
                entry.Entity.LastModifiedBy = currentUserId;
            }

            // 2. Handle Soft Delete logic
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified; // Cancel physical delete
                entry.Entity.GcRecord = 1;          // Mark as deleted
                entry.Entity.LastModified = utcNow;
                entry.Entity.LastModifiedBy = currentUserId;
            }

            // 3. Handle Automated Multi-tenancy
            // If the entity inherits from BaseTenantEntity, we inject the CompanyId automatically
            if (entry.State == EntityState.Added && entry.Entity is BaseTenantEntity tenantEntity)
            {
                // Only inject if it wasn't manually set (useful for SuperAdmin tasks)
                if (tenantEntity.CompanyId == Guid.Empty)
                {
                    tenantEntity.CompanyId = companyId;
                }
            }
        }
    }
}



/// <summary>
/// Provides extension methods for Entity Framework Core Change Tracking.
/// These helpers enhance the capabilities of interceptors and repositories 
/// by providing deeper insights into entity states.
/// </summary>
public static class Extensions
{

    /// <summary>
    /// Determines if any "Owned Entities" (Value Objects) associated with the current entity 
    /// have been added or modified.
    /// </summary>
    /// <remarks>
    /// In EF Core, when a property of an Owned Entity changes, the parent entity's state 
    /// might remain 'Unchanged'. This method ensures that the audit logic detects these 
    /// nested changes to correctly update 'LastModified' timestamps.
    /// </remarks>
    /// <param name="entry">The EntityEntry being tracked by the DbContext.</param>
    /// <returns>True if any associated owned entity has pending changes; otherwise, false.</returns>
    public static bool HasChangedOwnedEntities(this EntityEntry entry) =>
        entry.References.Any(r => 
            r.TargetEntry != null && 
            r.TargetEntry.Metadata.IsOwned() && 
            (r.TargetEntry.State == EntityState.Added || r.TargetEntry.State == EntityState.Modified));
    
}
