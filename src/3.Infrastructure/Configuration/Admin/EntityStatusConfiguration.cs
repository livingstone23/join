// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="EntityStatus"/> entity.
/// Defines the table for the catalog of operational states (e.g., Active, Paused).
/// </summary>
public class EntityStatusConfiguration : IEntityTypeConfiguration<EntityStatus>
{
    /// <summary>
    /// Configures the <see cref="EntityStatus"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<EntityStatus> builder)
    {
        // Map to table "EntityStatuses" in schema "Admin"
        builder.ToTable("EntityStatuses", "Admin");

        // Set the primary key
        builder.HasKey(es => es.Id);

        // --- Properties ---

        builder.Property(es => es.Name).IsRequired().HasMaxLength(50);
        builder.Property(es => es.Description).HasMaxLength(200);
        builder.Property(es => es.Code).IsRequired();

        // --- Relationships ---
        
        // One-to-many relationship with Project
        builder.HasMany(es => es.Projects)
            .WithOne(p => p.Status)
            .HasForeignKey(p => p.EntityStatusId);
            
        // One-to-many relationship with Area
        builder.HasMany(es => es.Areas)
            .WithOne(a => a.Status)
            .HasForeignKey(a => a.EntityStatusId);

        // --- Indexes ---

        // Unique index on the 'Code' property
        builder.HasIndex(es => es.Code).IsUnique();
        
        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(es => es.GcRecord == 0);
    }
}
