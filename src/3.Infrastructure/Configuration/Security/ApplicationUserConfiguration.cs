// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Security;



/// <summary>
/// Configures the database mapping for the <see cref="ApplicationUser"/> entity.
/// Defines schema, constraints, and global query filters for user accounts.
/// </summary>
public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{

    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    /// <param name="builder">The builder used to construct the Entity Framework model.</param>
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // 1. Table & Schema Mapping
        // Maps the entity to the "Users" table explicitly within the "Security" schema.
        builder.ToTable("Users", "Security");

        // 2. Property Constraints
        // Limits the URL length to optimize database storage and indexing.
        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(1024);

        // 3. Global Query Filters (CRITICAL)
        // Combines both conditions into a single filter. 
        // Ensures that queries will NEVER return soft-deleted (GcRecord > 0) OR inactive users.
        builder.HasQueryFilter(u => u.GcRecord == 0 && u.IsActive);
    }
    
}