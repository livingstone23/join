// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="SystemOption"/> entity.
/// Defines the structure for individual menu items, screens, or UI actions.
/// </summary>
public class SystemOptionConfiguration : IEntityTypeConfiguration<SystemOption>
{
    /// <summary>
    /// Configures the <see cref="SystemOption"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<SystemOption> builder)
    {
        // Map to table "SystemOptions" in schema "Admin"
        builder.ToTable("SystemOptions", "Admin");

        // Set the primary key
        builder.HasKey(o => o.Id);

        // --- Properties ---
        builder.Property(o => o.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(o => o.Route)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(o => o.Icon)
            .HasMaxLength(100);

        // Configure default values for supported actions.
        builder.Property(o => o.CanRead).HasDefaultValue(true);
        builder.Property(o => o.CanCreate).HasDefaultValue(true);
        builder.Property(o => o.CanUpdate).HasDefaultValue(true);
        builder.Property(o => o.CanDelete).HasDefaultValue(true);

        // --- Relationships ---

        // Required relationship with SystemModule
        // An option must belong to a module.
        builder.HasOne(o => o.Module)
            .WithMany(m => m.SystemOptions)
            .HasForeignKey(o => o.ModuleId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a module if it has options.

        // Self-referencing relationship for hierarchical menus (Parent-Child)
        // An option can have one parent.
        builder.HasOne(o => o.Parent)
            .WithMany(p => p.Children) // A parent can have many children.
            .HasForeignKey(o => o.ParentId)
            .OnDelete(DeleteBehavior.Restrict); // Avoids deleting a parent if children exist.

        // --- Indexes ---

        // Unique index to ensure a route is unique within a module.
        builder.HasIndex(o => new { o.ModuleId, o.Route }).IsUnique();
        
        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(o => o.GcRecord == 0);
    }
}
