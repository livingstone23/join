// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="SystemModule"/> entity.
/// Defines table structure, constraints, and relationships for system modules.
/// </summary>
public class SystemModuleConfiguration : IEntityTypeConfiguration<SystemModule>
{
    /// <summary>
    /// Configures the <see cref="SystemModule"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<SystemModule> builder)
    {
        // Map to table "SystemModules" in schema "Admin"
        builder.ToTable("SystemModules", "Admin");

        // Set the primary key
        builder.HasKey(m => m.Id);

        // --- Properties ---
        builder.Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(m => m.Description)
            .HasMaxLength(500);

        builder.Property(m => m.Icon)
            .HasMaxLength(100);

        builder.Property(m => m.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Relationships ---

        // One-to-many relationship with SystemOption
        // A SystemModule can contain multiple SystemOptions.
        builder.HasMany(m => m.SystemOptions)
            .WithOne(o => o.Module)
            .HasForeignKey(o => o.ModuleId)
            .OnDelete(DeleteBehavior.Cascade); // Deleting a module also deletes its options.

        // --- Indexes ---

        // Unique index on the module name to avoid duplicates.
        builder.HasIndex(m => m.Name).IsUnique();

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(m => m.GcRecord == 0);
    }
}
