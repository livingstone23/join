using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace JOIN.Infrastructure.Contexts;



/// <summary>
/// Custom EF Core SaveChanges interceptor that automatically handles auditing fields 
/// (Created/Modified dates and users) and enforces Soft Delete (GcRecord) on all auditable entities.
/// </summary>
public class AuditableEntitySaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditableEntitySaveChangesInterceptor"/> class.
    /// </summary>
    /// <param name="currentUserService">Service to retrieve the currently authenticated user's ID.</param>
    public AuditableEntitySaveChangesInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Intercepts the synchronous save operation to inject audit and soft-delete logic.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, 
        InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// Intercepts the asynchronous save operation to inject audit and soft-delete logic.
    /// </summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Iterates through the ChangeTracker to apply audit rules based on the entity's state.
    /// </summary>
    /// <param name="context">The active database context.</param>
    private void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        // Retrieve the current user ID, defaulting to "System" if the action is triggered 
        // by a background worker or an unauthenticated context.
        var currentUserId = _currentUserService.UserId ?? "System";
        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<BaseAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.Created = utcNow;
                    entry.Entity.CreatedBy = currentUserId;
                    entry.Entity.LastModified = utcNow;
                    entry.Entity.LastModifiedBy = currentUserId;
                    // Ensure a new entity is always explicitly marked as active (Not deleted)
                    entry.Entity.GcRecord = 0; 
                    break;

                case EntityState.Modified:
                    entry.Entity.LastModified = utcNow;
                    entry.Entity.LastModifiedBy = currentUserId;
                    break;

                case EntityState.Deleted:
                    // 1. Prevent physical deletion from the database
                    entry.State = EntityState.Modified;
                    
                    // 2. Mark as soft-deleted
                    entry.Entity.GcRecord = 1; 
                    
                    // 3. Record who performed the deletion and when
                    entry.Entity.LastModified = utcNow;
                    entry.Entity.LastModifiedBy = currentUserId;
                    break;
            }
        }
    }
}
